using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Crypto;

internal static class CmafSampleDecryptionUtil
{
    private const int AesBlockSize = 16;
    private static readonly byte[] WidevineSystemId = [0xED, 0xEF, 0x8B, 0xA9, 0x79, 0xD6, 0x4A, 0xCE, 0xA3, 0xC8, 0x27, 0xDC, 0xD5, 0x1D, 0x21, 0xED];
    private static readonly HashSet<string> ContainerBoxes = ["moov", "trak", "mdia", "minf", "stbl", "sinf", "schi", "moof", "traf"];
    private static readonly HashSet<string> VideoSampleEntries = ["avc1", "avc3", "hvc1", "hev1", "dvhe", "dvh1", "encv"];
    private static readonly HashSet<string> AudioSampleEntries = ["mp4a", "enca", "ac-3", "ec-3"];
    private static readonly HashSet<string> FragmentEncryptionBoxes = ["senc", "saiz", "saio"];

    internal static bool TryDecryptFile(string source, string dest, string keyHex, string? init, out string? error)
    {
        if (!HexUtil.TryParseHexString(keyHex, out var keyBytes) || keyBytes is not { Length: AesBlockSize })
        {
            error = "CMAF decrypt requires a 16-byte AES key.";
            return false;
        }

        return TryDecryptFile(source, dest, keyBytes, init, out error);
    }

    internal static bool TryDecryptFile(string source, string dest, byte[] key, string? init, out string? error)
    {
        if (key.Length != AesBlockSize)
        {
            error = "CMAF decrypt requires a 16-byte AES key.";
            return false;
        }

        var sourceData = File.ReadAllBytes(source);
        var initData = !string.IsNullOrEmpty(init) && File.Exists(init) ? File.ReadAllBytes(init) : sourceData;

        if (!TryReadInitInfo(initData, out var initInfo, out error))
            return false;

        if (!initInfo.IsSupportedScheme)
        {
            error = $"Unsupported CMAF scheme: {initInfo.Scheme ?? "<none>"}";
            return false;
        }

        if (!HasTopLevelBox(sourceData, "mdat"))
        {
            WriteOutput(dest, sourceData);
            error = null;
            return true;
        }

        var output = (byte[])sourceData.Clone();
        if (!TryDecryptFragment(output, initInfo, key, out _, out error))
            return false;

        SanitizeDecryptedFragment(output);
        SanitizeDecryptedInit(output);

        WriteOutput(dest, output);
        error = null;
        return true;
    }

    internal static bool TrySanitizeDecryptedInitFile(string path, out string? error)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            if (SanitizeDecryptedInit(data))
                WriteOutput(path, data);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static bool TryReadInitInfo(byte[] data, out CmafInitInfo info, out string? error)
    {
        var entries = new List<SampleEntryInfo>();
        string? psshKid = null;

        WalkBoxes(data, 0, data.Length, box =>
        {
            if (box.Type == "stsd")
                entries.AddRange(ParseStsd(data, box));
            else if (box.Type == "pssh")
                psshKid ??= TryReadWidevineKidFromPssh(data, box);
        });

        var encryptedEntry = entries.FirstOrDefault(e => e.Tenc != null);
        if (encryptedEntry?.Tenc == null)
        {
            error = "No encrypted sample entry with tenc was found.";
            info = new CmafInitInfo();
            return false;
        }

        var clearIndex = encryptedEntry.OriginalFormat == null
            ? null
            : entries.FirstOrDefault(e => e.Index != encryptedEntry.Index && e.Type == encryptedEntry.OriginalFormat)?.Index;

        info = new CmafInitInfo
        {
            Scheme = encryptedEntry.Scheme,
            EncryptedSampleDescriptionIndex = encryptedEntry.Index,
            ClearSampleDescriptionIndex = clearIndex,
            OriginalFormat = encryptedEntry.OriginalFormat,
            DefaultKid = encryptedEntry.Tenc.DefaultKid,
            CryptByteBlock = encryptedEntry.Tenc.CryptByteBlock,
            SkipByteBlock = encryptedEntry.Tenc.SkipByteBlock,
            IsProtected = encryptedEntry.Tenc.IsProtected,
            PerSampleIvSize = encryptedEntry.Tenc.PerSampleIvSize,
            DefaultConstantIv = encryptedEntry.Tenc.DefaultConstantIv,
            PsshKid = psshKid
        };
        error = null;
        return true;
    }

    internal static bool TryDecryptFragment(byte[] data, CmafInitInfo initInfo, byte[] key, out bool decryptedAny, out string? error)
    {
        decryptedAny = false;
        if (key.Length != AesBlockSize)
        {
            error = "CMAF decrypt requires a 16-byte AES key.";
            return false;
        }

        var topLevelBoxes = ReadBoxes(data, 0, data.Length).ToList();
        var moofBoxes = topLevelBoxes.Where(b => b.Type == "moof").ToList();
        if (moofBoxes.Count == 0)
        {
            error = "Fragment must contain at least one moof box.";
            return false;
        }

        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        using var blockDecryptor = aes.CreateDecryptor();
        var cipherBlock = new byte[AesBlockSize];
        var plainBlock = new byte[AesBlockSize];

        foreach (var moof in moofBoxes)
        {
            var mdat = topLevelBoxes.FirstOrDefault(b => b.Start > moof.Start && b.Type == "mdat");
            if (mdat.Type == null)
            {
                error = "moof has no following mdat box.";
                return false;
            }

            foreach (var traf in ReadBoxes(data, moof.ContentStart, moof.End).Where(b => b.Type == "traf"))
            {
                if (!TryReadTraf(data, traf, initInfo, out var trafInfo, out error))
                    return false;

                var selectedIndex = trafInfo.SampleDescriptionIndex ?? initInfo.EncryptedSampleDescriptionIndex;
                if (selectedIndex != initInfo.EncryptedSampleDescriptionIndex)
                    continue;

                if (trafInfo.SencSamples.Count == 0)
                {
                    error = "Encrypted CMAF fragment is missing senc sample encryption data.";
                    return false;
                }

                if (trafInfo.SampleSizes.Count != trafInfo.SencSamples.Count)
                {
                    error = $"trun sample count ({trafInfo.SampleSizes.Count}) does not match senc sample count ({trafInfo.SencSamples.Count}).";
                    return false;
                }

                var sampleOffset = trafInfo.DataOffset.HasValue ? moof.Start + trafInfo.DataOffset.Value : mdat.ContentStart;
                for (var i = 0; i < trafInfo.SampleSizes.Count; i++)
                {
                    var sampleSize = checked((int)trafInfo.SampleSizes[i]);
                    if (sampleOffset < mdat.ContentStart || sampleOffset + sampleSize > mdat.End)
                    {
                        error = "Sample data range is outside mdat.";
                        return false;
                    }

                    var sample = trafInfo.SencSamples[i];
                    var ivSource = sample.Iv ?? initInfo.DefaultConstantIv;
                    if (ivSource is not { Length: > 0 })
                    {
                        error = "CMAF sample has no per-sample IV and init has no default constant IV.";
                        return false;
                    }

                    var iv = PadIv(ivSource);
                    if (sample.Subsamples.Count == 0)
                    {
                        DecryptProtectedRange(data, sampleOffset, sampleSize, initInfo, blockDecryptor, iv, cipherBlock, plainBlock);
                    }
                    else
                    {
                        var subsampleOffset = sampleOffset;
                        foreach (var subsample in sample.Subsamples)
                        {
                            subsampleOffset += subsample.ClearBytes;
                            if (subsampleOffset < sampleOffset || subsampleOffset + subsample.ProtectedBytes > sampleOffset + sampleSize)
                            {
                                error = "Subsample clear/protected layout exceeds sample size.";
                                return false;
                            }

                            DecryptProtectedRange(data, subsampleOffset, subsample.ProtectedBytes, initInfo, blockDecryptor, iv, cipherBlock, plainBlock);
                            subsampleOffset += subsample.ProtectedBytes;
                        }
                    }

                    sampleOffset += sampleSize;
                }

                if (trafInfo.SampleDescriptionIndexFieldOffset.HasValue && initInfo.ClearSampleDescriptionIndex.HasValue)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(
                        data.AsSpan(trafInfo.SampleDescriptionIndexFieldOffset.Value, 4),
                        (uint)initInfo.ClearSampleDescriptionIndex.Value);
                }

                decryptedAny = true;
            }
        }

        error = null;
        return true;
    }

    private static bool TryReadTraf(byte[] data, Box traf, CmafInitInfo initInfo, out TrafInfo info, out string? error)
    {
        uint? defaultSampleSize = null;
        var sampleDescriptionIndex = (int?)null;
        var sampleDescriptionIndexFieldOffset = (int?)null;
        var sampleSizes = new List<uint>();
        var sencSamples = new List<SencSample>();
        var dataOffset = (int?)null;

        foreach (var box in ReadBoxes(data, traf.ContentStart, traf.End))
        {
            switch (box.Type)
            {
                case "tfhd":
                    ReadTfhd(data, box, out sampleDescriptionIndex, out sampleDescriptionIndexFieldOffset, out defaultSampleSize);
                    break;
                case "trun":
                    ReadTrun(data, box, defaultSampleSize, out dataOffset, sampleSizes);
                    break;
                case "senc":
                    sencSamples = ReadSenc(data, box, initInfo.PerSampleIvSize);
                    break;
            }
        }

        info = new TrafInfo
        {
            SampleDescriptionIndex = sampleDescriptionIndex,
            SampleDescriptionIndexFieldOffset = sampleDescriptionIndexFieldOffset,
            DataOffset = dataOffset,
            SampleSizes = sampleSizes,
            SencSamples = sencSamples
        };
        error = null;
        return true;
    }

    private static void ReadTfhd(byte[] data, Box box, out int? sampleDescriptionIndex, out int? sampleDescriptionIndexFieldOffset, out uint? defaultSampleSize)
    {
        sampleDescriptionIndex = null;
        sampleDescriptionIndexFieldOffset = null;
        defaultSampleSize = null;

        var flags = FullBoxFlags(data, box);
        var offset = box.ContentStart + 4 + 4;
        if ((flags & 0x000001) != 0) offset += 8;
        if ((flags & 0x000002) != 0)
        {
            sampleDescriptionIndexFieldOffset = offset;
            sampleDescriptionIndex = checked((int)ReadUInt32(data, offset));
            offset += 4;
        }
        if ((flags & 0x000008) != 0) offset += 4;
        if ((flags & 0x000010) != 0)
            defaultSampleSize = ReadUInt32(data, offset);
    }

    private static void ReadTrun(byte[] data, Box box, uint? defaultSampleSize, out int? dataOffset, List<uint> sampleSizes)
    {
        dataOffset = null;
        var version = data[box.ContentStart];
        var flags = FullBoxFlags(data, box);
        var offset = box.ContentStart + 4;
        var sampleCount = ReadUInt32(data, offset);
        offset += 4;

        if ((flags & 0x000001) != 0)
        {
            dataOffset = ReadInt32(data, offset);
            offset += 4;
        }
        if ((flags & 0x000004) != 0) offset += 4;

        for (var i = 0; i < sampleCount; i++)
        {
            if ((flags & 0x000100) != 0) offset += 4;
            if ((flags & 0x000200) != 0)
            {
                sampleSizes.Add(ReadUInt32(data, offset));
                offset += 4;
            }
            else if (defaultSampleSize.HasValue)
            {
                sampleSizes.Add(defaultSampleSize.Value);
            }
            if ((flags & 0x000400) != 0) offset += 4;
            if ((flags & 0x000800) != 0) offset += 4;
        }

        _ = version;
    }

    private static List<SencSample> ReadSenc(byte[] data, Box box, int perSampleIvSize)
    {
        var flags = FullBoxFlags(data, box);
        var offset = box.ContentStart + 4;
        var sampleCount = ReadUInt32(data, offset);
        offset += 4;
        var hasSubsamples = (flags & 0x000002) != 0;
        var samples = new List<SencSample>(checked((int)sampleCount));

        for (var i = 0; i < sampleCount; i++)
        {
            byte[]? iv = null;
            if (perSampleIvSize > 0)
            {
                iv = data[offset..(offset + perSampleIvSize)];
                offset += perSampleIvSize;
            }

            var subsamples = new List<SubsampleLayout>();
            if (hasSubsamples)
            {
                var subsampleCount = ReadUInt16(data, offset);
                offset += 2;
                for (var j = 0; j < subsampleCount; j++)
                {
                    var clear = ReadUInt16(data, offset);
                    var protectedBytes = ReadUInt32(data, offset + 2);
                    offset += 6;
                    subsamples.Add(new SubsampleLayout(clear, checked((int)protectedBytes)));
                }
            }

            samples.Add(new SencSample(iv, subsamples));
        }

        return samples;
    }

    private static List<SampleEntryInfo> ParseStsd(byte[] data, Box box)
    {
        var entries = new List<SampleEntryInfo>();
        var offset = box.ContentStart + 4;
        if (offset + 4 > box.End)
            return entries;

        var entryCount = ReadUInt32(data, offset);
        offset += 4;
        for (var i = 0; i < entryCount && offset + 8 <= box.End; i++)
        {
            var entrySize = checked((int)ReadUInt32(data, offset));
            var entryType = FourCc(data.AsSpan(offset + 4, 4));
            var entryEnd = offset + entrySize;
            if (entrySize < 8 || entryEnd > box.End)
                break;

            var entry = new SampleEntryInfo { Index = i + 1, Type = entryType };
            var childStart = GetSampleEntryChildStart(offset, entryEnd, entryType);
            if (childStart < entryEnd)
            {
                WalkBoxes(data, childStart, entryEnd, child =>
                {
                    if (child.Type == "frma" && child.ContentStart + 4 <= child.End)
                        entry.OriginalFormat = FourCc(data.AsSpan(child.ContentStart, 4));
                    else if (child.Type == "schm" && child.ContentStart + 12 <= child.End)
                        entry.Scheme = FourCc(data.AsSpan(child.ContentStart + 4, 4));
                    else if (child.Type == "tenc")
                        entry.Tenc = ReadTenc(data, child);
                });
            }

            entries.Add(entry);
            offset = entryEnd;
        }

        return entries;
    }

    private static int GetSampleEntryChildStart(int entryStart, int entryEnd, string entryType)
    {
        if (VideoSampleEntries.Contains(entryType))
            return Math.Min(entryStart + 8 + 78, entryEnd);
        if (AudioSampleEntries.Contains(entryType))
            return Math.Min(entryStart + 8 + 28, entryEnd);
        return entryStart + 8;
    }

    private static TencInfo ReadTenc(byte[] data, Box box)
    {
        var version = data[box.ContentStart];
        var offset = box.ContentStart + 4;
        int crypt = 0;
        int skip = 0;
        byte isProtected;
        byte perSampleIvSize;
        byte[] kid;

        if (version >= 1)
        {
            var pattern = data[offset + 1];
            crypt = pattern >> 4;
            skip = pattern & 0x0F;
            isProtected = data[offset + 2];
            perSampleIvSize = data[offset + 3];
            kid = data[(offset + 4)..(offset + 20)];
            offset += 20;
        }
        else
        {
            isProtected = data[offset + 1];
            perSampleIvSize = data[offset + 2];
            kid = data[(offset + 3)..(offset + 19)];
            offset += 19;
        }

        byte[]? constantIv = null;
        if (perSampleIvSize == 0 && offset < box.End)
        {
            var ivSize = data[offset];
            offset += 1;
            if (offset + ivSize <= box.End)
                constantIv = data[offset..(offset + ivSize)];
        }

        return new TencInfo(crypt, skip, isProtected, perSampleIvSize, kid, constantIv);
    }

    private static void DecryptProtectedRange(byte[] data, int start, int protectedSize, CmafInitInfo initInfo, ICryptoTransform blockDecryptor, byte[] iv, byte[] cipherBlock, byte[] plainBlock)
    {
        if (protectedSize <= 0)
            return;

        if (initInfo.CryptByteBlock >= 1 && initInfo.SkipByteBlock >= 1)
        {
            var cryptLength = initInfo.CryptByteBlock * AesBlockSize;
            var step = (initInfo.CryptByteBlock + initInfo.SkipByteBlock) * AesBlockSize;
            for (var position = 0; position + cryptLength <= protectedSize; position += step)
                DecryptCbcInPlace(data, start + position, cryptLength, blockDecryptor, iv, cipherBlock, plainBlock);
        }
        else
        {
            DecryptCbcInPlace(data, start, protectedSize & ~(AesBlockSize - 1), blockDecryptor, iv, cipherBlock, plainBlock);
        }
    }

    private static void DecryptCbcInPlace(byte[] data, int start, int length, ICryptoTransform blockDecryptor, byte[] iv, byte[] cipherBlock, byte[] plainBlock)
    {
        if (length <= 0)
            return;

        for (var offset = start; offset < start + length; offset += AesBlockSize)
        {
            Buffer.BlockCopy(data, offset, cipherBlock, 0, AesBlockSize);
            blockDecryptor.TransformBlock(cipherBlock, 0, AesBlockSize, plainBlock, 0);
            for (var i = 0; i < AesBlockSize; i++)
                data[offset + i] = (byte)(plainBlock[i] ^ iv[i]);
            Buffer.BlockCopy(cipherBlock, 0, iv, 0, AesBlockSize);
        }
    }

    private static byte[] PadIv(byte[] iv)
    {
        var padded = new byte[AesBlockSize];
        Buffer.BlockCopy(iv, 0, padded, 0, Math.Min(iv.Length, AesBlockSize));
        return padded;
    }

    private static void WalkBoxes(byte[] data, int start, int end, Action<Box> visitor)
    {
        foreach (var box in ReadBoxes(data, start, end))
        {
            visitor(box);
            if (ContainerBoxes.Contains(box.Type))
                WalkBoxes(data, box.ContentStart, box.End, visitor);
        }
    }

    private static IEnumerable<Box> ReadBoxes(byte[] data, int start, int end)
    {
        var offset = start;
        while (offset + 8 <= end)
        {
            var size = (long)ReadUInt32(data, offset);
            var headerSize = 8;
            var type = FourCc(data.AsSpan(offset + 4, 4));
            if (size == 1)
            {
                if (offset + 16 > end)
                    yield break;
                size = checked((long)ReadUInt64(data, offset + 8));
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = end - offset;
            }

            if (size < headerSize || offset + size > end || size > int.MaxValue)
                yield break;

            yield return new Box(offset, checked((int)size), headerSize, type);
            offset += checked((int)size);
        }
    }

    private static bool HasTopLevelBox(byte[] data, string type)
    {
        return ReadBoxes(data, 0, data.Length).Any(box => box.Type == type);
    }

    private static string? TryReadWidevineKidFromPssh(byte[] data, Box box)
    {
        if (box.ContentStart + 24 > box.End)
            return null;

        var systemId = data.AsSpan(box.ContentStart + 4, 16);
        if (!systemId.SequenceEqual(WidevineSystemId))
            return null;

        var offset = box.ContentStart + 20;
        if (data[box.ContentStart] > 0)
        {
            if (offset + 4 > box.End)
                return null;
            var kidCount = ReadUInt32(data, offset);
            offset += 4;
            if (kidCount > 0 && offset + 16 <= box.End)
                return HexUtil.BytesToHex(data[offset..(offset + 16)]).ToLower();
            offset += checked((int)kidCount) * 16;
        }

        if (offset + 4 > box.End)
            return null;

        var dataSize = ReadUInt32(data, offset);
        offset += 4;
        if (dataSize >= 18 && offset + 18 <= box.End && data[offset] == 0x12 && data[offset + 1] == 0x10)
            return HexUtil.BytesToHex(data[(offset + 2)..(offset + 18)]).ToLower();

        return null;
    }

    private static bool SanitizeDecryptedInit(byte[] data)
    {
        var changed = false;
        WalkBoxes(data, 0, data.Length, box =>
        {
            if (box.Type == "pssh")
            {
                WriteFourCc(data, box.Start + 4, "free");
                changed = true;
            }
        });

        return changed;
    }

    private static bool SanitizeDecryptedFragment(byte[] data)
    {
        return MarkBoxesFree(data, 0, data.Length, boxType => FragmentEncryptionBoxes.Contains(boxType));
    }

    private static bool MarkBoxesFree(byte[] data, int start, int end, Func<string, bool> shouldFree)
    {
        var changed = false;
        foreach (var box in ReadBoxes(data, start, end))
        {
            if (shouldFree(box.Type))
            {
                WriteFourCc(data, box.Start + 4, "free");
                changed = true;
                continue;
            }

            if (ContainerBoxes.Contains(box.Type))
                changed |= MarkBoxesFree(data, box.ContentStart, box.End, shouldFree);
        }

        return changed;
    }

    private static void WriteFourCc(byte[] data, int offset, string value)
    {
        Encoding.ASCII.GetBytes(value).CopyTo(data.AsSpan(offset, 4));
    }

    private static void WriteOutput(string dest, byte[] data)
    {
        var dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(dest, data);
    }

    private static uint FullBoxFlags(byte[] data, Box box)
    {
        return (uint)((data[box.ContentStart + 1] << 16) | (data[box.ContentStart + 2] << 8) | data[box.ContentStart + 3]);
    }

    private static ushort ReadUInt16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
    private static uint ReadUInt32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    private static ulong ReadUInt64(byte[] data, int offset) => BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset, 8));
    private static int ReadInt32(byte[] data, int offset) => BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
    private static string FourCc(ReadOnlySpan<byte> data) => Encoding.ASCII.GetString(data);

    internal sealed class CmafInitInfo
    {
        public string? Scheme { get; init; }
        public int EncryptedSampleDescriptionIndex { get; init; }
        public int? ClearSampleDescriptionIndex { get; init; }
        public string? OriginalFormat { get; init; }
        public byte[]? DefaultKid { get; init; }
        public string? PsshKid { get; init; }
        public int CryptByteBlock { get; init; }
        public int SkipByteBlock { get; init; }
        public int IsProtected { get; init; }
        public int PerSampleIvSize { get; init; }
        public byte[]? DefaultConstantIv { get; init; }
        public bool IsSupportedScheme => Scheme is "cbcs" or "cbc1";
    }

    private sealed class SampleEntryInfo
    {
        public int Index { get; init; }
        public string Type { get; init; } = "";
        public string? OriginalFormat { get; set; }
        public string? Scheme { get; set; }
        public TencInfo? Tenc { get; set; }
    }

    private sealed record TencInfo(int CryptByteBlock, int SkipByteBlock, int IsProtected, int PerSampleIvSize, byte[] DefaultKid, byte[]? DefaultConstantIv);
    private sealed record SencSample(byte[]? Iv, List<SubsampleLayout> Subsamples);
    private readonly record struct SubsampleLayout(int ClearBytes, int ProtectedBytes);

    private sealed class TrafInfo
    {
        public int? SampleDescriptionIndex { get; init; }
        public int? SampleDescriptionIndexFieldOffset { get; init; }
        public int? DataOffset { get; init; }
        public List<uint> SampleSizes { get; init; } = [];
        public List<SencSample> SencSamples { get; init; } = [];
    }

    private readonly record struct Box(int Start, int Size, int HeaderSize, string Type)
    {
        public int ContentStart => Start + HeaderSize;
        public int End => Start + Size;
    }
}
