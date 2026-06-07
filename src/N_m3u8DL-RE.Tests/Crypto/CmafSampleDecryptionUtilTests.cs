using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Mp4SubtitleParser;
using N_m3u8DL_RE.Crypto;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Crypto;

public class CmafSampleDecryptionUtilTests
{
    [Fact]
    public void SelectDecryptionEngine_Auto_UsesInternalCmafForCbcs()
    {
        var kid = Enumerable.Range(32, 16).Select(i => (byte)i).ToArray();
        var iv = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();
        var init = BuildInit(kid, iv);

        var cbcsInfo = MP4InitUtil.ReadInit(init);
        Assert.Equal(DecryptEngine.CMAF, MP4DecryptUtil.SelectDecryptionEngine(DecryptEngine.AUTO, cbcsInfo));

        var cencInfo = new ParsedMP4Info { Scheme = "cenc" };
        Assert.Equal(DecryptEngine.MP4DECRYPT, MP4DecryptUtil.SelectDecryptionEngine(DecryptEngine.AUTO, cencInfo));
    }

    [Fact]
    public async Task DecryptAsync_CmafWithSingleKey_DecryptsUsingDetectedKid()
    {
        var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var iv = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();
        var kid = Enumerable.Range(32, 16).Select(i => (byte)i).ToArray();
        var init = BuildInit(kid, iv);

        var clearPrefix = Encoding.ASCII.GetBytes("hdr");
        var clearSuffix = Encoding.ASCII.GetBytes("tail");
        var plain = Enumerable.Repeat((byte)'Z', 16).ToArray();
        var encrypted = EncryptCbc(key, iv, plain);
        var sampleData = Concat(clearPrefix, encrypted, clearSuffix);
        var fragment = BuildFragment(sampleData, clearPrefix.Length, encrypted.Length, clearSuffix.Length);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var initPath = Path.Combine(tempDir, "init.mp4");
            var sourcePath = Path.Combine(tempDir, "seg.m4s");
            var destPath = Path.Combine(tempDir, "seg_dec.m4s");
            await File.WriteAllBytesAsync(initPath, init);
            await File.WriteAllBytesAsync(sourcePath, fragment);

            var keyHex = Convert.ToHexString(key).ToLowerInvariant();
            var kidHex = Convert.ToHexString(kid).ToLowerInvariant();
            var success = await MP4DecryptUtil.DecryptAsync(
                DecryptEngine.CMAF,
                "",
                [keyHex],
                sourcePath,
                destPath,
                kidHex,
                initPath);

            Assert.True(success);
            var output = await File.ReadAllBytesAsync(destPath);
            var mdat = FindBox(output, "mdat");
            Assert.Equal(Concat(clearPrefix, plain, clearSuffix), output[mdat.contentStart..(mdat.contentStart + sampleData.Length)]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryDecryptFragment_CbcsCrypt1Skip9_DecryptsPatternAndPatchesSampleDescriptionIndex()
    {
        var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var iv = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();
        var kid = Enumerable.Range(32, 16).Select(i => (byte)i).ToArray();
        var init = BuildInit(kid, iv);

        Assert.True(CmafSampleDecryptionUtil.TryReadInitInfo(init, out var initInfo, out var initError), initError);
        Assert.Equal("cbcs", initInfo.Scheme);
        Assert.Equal(1, initInfo.EncryptedSampleDescriptionIndex);
        Assert.Equal(2, initInfo.ClearSampleDescriptionIndex);
        Assert.Equal(1, initInfo.CryptByteBlock);
        Assert.Equal(9, initInfo.SkipByteBlock);
        Assert.Equal(0, initInfo.PerSampleIvSize);
        Assert.Equal(iv, initInfo.DefaultConstantIv);

        var clearPrefix = Encoding.ASCII.GetBytes("clear");
        var clearSuffix = Encoding.ASCII.GetBytes("end");
        var firstEncryptedBlockPlain = Enumerable.Repeat((byte)'A', 16).ToArray();
        var skippedBlocks = Enumerable.Repeat((byte)'S', 9 * 16).ToArray();
        var secondEncryptedBlockPlain = Enumerable.Repeat((byte)'B', 16).ToArray();
        var protectedTail = Enumerable.Repeat((byte)'T', 7).ToArray();
        var firstEncryptedBlock = EncryptCbc(key, iv, firstEncryptedBlockPlain);
        var secondEncryptedBlock = EncryptCbc(key, firstEncryptedBlock[^16..], secondEncryptedBlockPlain);
        var protectedBytes = Concat(firstEncryptedBlock, skippedBlocks, secondEncryptedBlock, protectedTail);
        var sampleData = Concat(clearPrefix, protectedBytes, clearSuffix);
        var fragment = BuildFragment(sampleData, clearPrefix.Length, protectedBytes.Length, clearSuffix.Length);

        Assert.True(CmafSampleDecryptionUtil.TryDecryptFragment(fragment, initInfo, key, out var decryptedAny, out var decryptError), decryptError);
        Assert.True(decryptedAny);

        var mdatPayloadStart = FindBox(fragment, "mdat").contentStart;
        var decryptedSample = fragment[mdatPayloadStart..];
        Assert.Equal(Concat(clearPrefix, firstEncryptedBlockPlain, skippedBlocks, secondEncryptedBlockPlain, protectedTail, clearSuffix), decryptedSample);

        var tfhd = FindBox(fragment, "tfhd");
        var sampleDescriptionIndex = BinaryPrimitives.ReadUInt32BigEndian(fragment.AsSpan(tfhd.contentStart + 8, 4));
        Assert.Equal(2u, sampleDescriptionIndex);
    }

    [Fact]
    public void TryDecryptFragment_MergedClearLeadAndEncryptedFragment_DecryptsEncryptedMoof()
    {
        var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var iv = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();
        var kid = Enumerable.Range(32, 16).Select(i => (byte)i).ToArray();
        var init = BuildInit(kid, iv);
        Assert.True(CmafSampleDecryptionUtil.TryReadInitInfo(init, out var initInfo, out var initError), initError);

        var clearLead = Encoding.ASCII.GetBytes("clear-lead-fragment");
        var clearFragment = BuildClearFragment(clearLead);

        var clearPrefix = Encoding.ASCII.GetBytes("hdr");
        var clearSuffix = Encoding.ASCII.GetBytes("tail");
        var firstEncryptedBlockPlain = Enumerable.Repeat((byte)'C', 16).ToArray();
        var skippedBlocks = Enumerable.Repeat((byte)'0', 9 * 16).ToArray();
        var secondEncryptedBlockPlain = Enumerable.Repeat((byte)'D', 16).ToArray();
        var firstEncryptedBlock = EncryptCbc(key, iv, firstEncryptedBlockPlain);
        var secondEncryptedBlock = EncryptCbc(key, firstEncryptedBlock[^16..], secondEncryptedBlockPlain);
        var protectedBytes = Concat(firstEncryptedBlock, skippedBlocks, secondEncryptedBlock);
        var encryptedSample = Concat(clearPrefix, protectedBytes, clearSuffix);
        var encryptedFragment = BuildFragment(encryptedSample, clearPrefix.Length, protectedBytes.Length, clearSuffix.Length);
        var merged = Concat(clearFragment, encryptedFragment);

        Assert.True(CmafSampleDecryptionUtil.TryDecryptFragment(merged, initInfo, key, out var decryptedAny, out var decryptError), decryptError);
        Assert.True(decryptedAny);

        var firstMdat = FindBox(merged, "mdat");
        Assert.Equal(clearLead, merged[firstMdat.contentStart..(firstMdat.contentStart + clearLead.Length)]);

        var secondMdat = FindBox(merged, "mdat", clearFragment.Length, merged.Length);
        var expectedEncryptedSample = Concat(clearPrefix, firstEncryptedBlockPlain, skippedBlocks, secondEncryptedBlockPlain, clearSuffix);
        Assert.Equal(expectedEncryptedSample, merged[secondMdat.contentStart..(secondMdat.contentStart + expectedEncryptedSample.Length)]);
    }

    [Fact]
    public void TryDecryptFile_CmafSanitizesFragmentEncryptionBoxes()
    {
        var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var iv = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();
        var kid = Enumerable.Range(32, 16).Select(i => (byte)i).ToArray();
        var init = BuildInit(kid, iv);

        var clearPrefix = Encoding.ASCII.GetBytes("hdr");
        var plain = Enumerable.Repeat((byte)'Z', 16).ToArray();
        var encrypted = EncryptCbc(key, iv, plain);
        var sampleData = Concat(clearPrefix, encrypted);
        var fragment = BuildFragment(sampleData, clearPrefix.Length, encrypted.Length, 0);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var initPath = Path.Combine(tempDir, "init.mp4");
            var sourcePath = Path.Combine(tempDir, "seg.m4s");
            var destPath = Path.Combine(tempDir, "seg_dec.m4s");
            File.WriteAllBytes(initPath, init);
            File.WriteAllBytes(sourcePath, fragment);

            Assert.True(CmafSampleDecryptionUtil.TryDecryptFile(sourcePath, destPath, key, initPath, out var error), error);
            var output = File.ReadAllBytes(destPath);

            Assert.Equal(0, FindBox(output, "senc").size);
            Assert.NotEqual(0, FindBox(output, "free").size);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryDecryptFile_MergedCmafFileWithoutExternalInit_StreamsAndDecryptsFragments()
    {
        var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var iv = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();
        var kid = Enumerable.Range(32, 16).Select(i => (byte)i).ToArray();
        var init = BuildInit(kid, iv, includePssh: true);

        var clearLead = Encoding.ASCII.GetBytes("clear-lead-fragment");
        var clearFragment = BuildClearFragment(clearLead);

        var clearPrefix = Encoding.ASCII.GetBytes("hdr");
        var clearSuffix = Encoding.ASCII.GetBytes("tail");
        var plain = Enumerable.Repeat((byte)'Q', 16).ToArray();
        var encrypted = EncryptCbc(key, iv, plain);
        var sampleData = Concat(clearPrefix, encrypted, clearSuffix);
        var encryptedFragment = BuildFragment(sampleData, clearPrefix.Length, encrypted.Length, clearSuffix.Length);
        var source = Concat(init, clearFragment, encryptedFragment);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "merged.mp4");
            var destPath = Path.Combine(tempDir, "merged_dec.mp4");
            File.WriteAllBytes(sourcePath, source);

            Assert.True(CmafSampleDecryptionUtil.TryDecryptFile(sourcePath, destPath, key, null, out var error), error);
            var output = File.ReadAllBytes(destPath);

            Assert.Equal(0, FindBox(output, "pssh").size);
            Assert.Equal(0, FindBox(output, "senc").size);
            Assert.NotEqual(0, FindBox(output, "free").size);

            var firstMdat = FindBox(output, "mdat", init.Length, output.Length);
            Assert.Equal(clearLead, output[firstMdat.contentStart..(firstMdat.contentStart + clearLead.Length)]);

            var secondMdat = FindBox(output, "mdat", init.Length + clearFragment.Length, output.Length);
            Assert.Equal(Concat(clearPrefix, plain, clearSuffix), output[secondMdat.contentStart..(secondMdat.contentStart + sampleData.Length)]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryDecryptFile_MdatWithoutMoof_Fails()
    {
        var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var iv = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();
        var kid = Enumerable.Range(32, 16).Select(i => (byte)i).ToArray();
        var init = BuildInit(kid, iv);
        var source = Concat(init, Box("mdat", Enumerable.Repeat((byte)'X', 16).ToArray()));

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "bad.mp4");
            var destPath = Path.Combine(tempDir, "bad_dec.mp4");
            File.WriteAllBytes(sourcePath, source);

            Assert.False(CmafSampleDecryptionUtil.TryDecryptFile(sourcePath, destPath, key, null, out var error));
            Assert.Contains("no preceding moof", error, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(destPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TrySanitizeDecryptedInitFile_FreesPsshWithoutChangingSampleEntry()
    {
        var iv = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();
        var kid = Enumerable.Range(32, 16).Select(i => (byte)i).ToArray();
        var init = BuildInit(kid, iv, includePssh: true);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var initPath = Path.Combine(tempDir, "init.mp4");
            File.WriteAllBytes(initPath, init);

            Assert.True(CmafSampleDecryptionUtil.TrySanitizeDecryptedInitFile(initPath, out var error), error);
            var output = File.ReadAllBytes(initPath);
            var stsd = FindBox(output, "stsd");
            var firstEntryOffset = stsd.contentStart + 8;

            Assert.Equal("encv", Encoding.ASCII.GetString(output, firstEntryOffset + 4, 4));
            Assert.Equal(0, FindBox(output, "pssh").size);
            Assert.NotEqual(0, FindBox(output, "sinf").size);
            Assert.NotEqual(0, FindBox(output, "free").size);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static byte[] BuildInit(byte[] kid, byte[] constantIv, bool includePssh = false)
    {
        var tencPayload = Concat(
            [0x00, 0x19, 0x01, 0x00],
            kid,
            [(byte)constantIv.Length],
            constantIv);
        var tenc = FullBox("tenc", 1, 0, tencPayload);
        var schi = Box("schi", tenc);
        var schm = FullBox("schm", 0, 0, Concat(FourCc("cbcs"), UInt32(0x00010000)));
        var frma = Box("frma", FourCc("dvhe"));
        var sinf = Box("sinf", frma, schm, schi);
        var encv = Box("encv", Concat(new byte[78], sinf));
        var dvhe = Box("dvhe", new byte[78]);
        var stsd = FullBox("stsd", 0, 0, Concat(UInt32(2), encv, dvhe));
        var trak = Box("trak", Box("mdia", Box("minf", Box("stbl", stsd))));
        if (!includePssh)
            return Box("moov", trak);

        var pssh = FullBox("pssh", 0, 0, Concat(
            [0xED, 0xEF, 0x8B, 0xA9, 0x79, 0xD6, 0x4A, 0xCE, 0xA3, 0xC8, 0x27, 0xDC, 0xD5, 0x1D, 0x21, 0xED],
            UInt32(0)));
        return Box("moov", trak, pssh);
    }

    private static byte[] BuildFragment(byte[] sampleData, int clearPrefixSize, int protectedSize, int clearSuffixSize)
    {
        byte[] BuildMoof(int dataOffset)
        {
            var tfhd = FullBox("tfhd", 0, 0x02000A, Concat(UInt32(1), UInt32(1), UInt32(1)));
            var trun = FullBox("trun", 0, 0x000201, Concat(UInt32(1), Int32(dataOffset), UInt32((uint)sampleData.Length)));
            var senc = FullBox("senc", 0, 0x000002, Concat(
                UInt32(1),
                UInt16(2),
                UInt16((ushort)clearPrefixSize),
                UInt32((uint)protectedSize),
                UInt16((ushort)clearSuffixSize),
                UInt32(0)));
            var traf = Box("traf", tfhd, trun, senc);
            return Box("moof", FullBox("mfhd", 0, 0, UInt32(1)), traf);
        }

        var probeMoof = BuildMoof(0);
        var moof = BuildMoof(probeMoof.Length + 8);
        return Concat(moof, Box("mdat", sampleData));
    }

    private static byte[] BuildClearFragment(byte[] sampleData)
    {
        byte[] BuildMoof(int dataOffset)
        {
            var tfhd = FullBox("tfhd", 0, 0x02000A, Concat(UInt32(1), UInt32(2), UInt32(1)));
            var trun = FullBox("trun", 0, 0x000201, Concat(UInt32(1), Int32(dataOffset), UInt32((uint)sampleData.Length)));
            var traf = Box("traf", tfhd, trun);
            return Box("moof", FullBox("mfhd", 0, 0, UInt32(1)), traf);
        }

        var probeMoof = BuildMoof(0);
        var moof = BuildMoof(probeMoof.Length + 8);
        return Concat(moof, Box("mdat", sampleData));
    }

    private static byte[] EncryptCbc(byte[] key, byte[] iv, byte[] plain)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plain, 0, plain.Length);
    }

    private static (int start, int size, int contentStart) FindBox(byte[] data, string type)
    {
        return FindBox(data, type, 0, data.Length);
    }

    private static (int start, int size, int contentStart) FindBox(byte[] data, string type, int start, int end)
    {
        var needle = FourCc(type);
        for (var offset = start; offset + 8 <= end;)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4)));
            if (size < 8 || offset + size > end)
                break;

            var boxType = Encoding.ASCII.GetString(data.AsSpan(offset + 4, 4));
            if (data.AsSpan(offset + 4, 4).SequenceEqual(needle))
                return (offset, size, offset + 8);

            if (boxType == "stsd")
            {
                var count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 12, 4));
                var entryOffset = offset + 16;
                for (var i = 0; i < count && entryOffset + 8 <= offset + size; i++)
                {
                    var entrySize = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entryOffset, 4)));
                    if (entrySize < 8 || entryOffset + entrySize > offset + size)
                        break;

                    var entryType = Encoding.ASCII.GetString(data.AsSpan(entryOffset + 4, 4));
                    if (entryType == type)
                        return (entryOffset, entrySize, entryOffset + 8);

                    var childStart = entryType is "mp4a" or "enca" or "ac-3" or "ec-3"
                        ? Math.Min(entryOffset + 36, entryOffset + entrySize)
                        : Math.Min(entryOffset + 86, entryOffset + entrySize);
                    var nested = FindBox(data, type, childStart, entryOffset + entrySize);
                    if (nested.size > 0)
                        return nested;

                    entryOffset += entrySize;
                }
            }

            if (boxType is "moov" or "trak" or "mdia" or "minf" or "stbl" or "sinf" or "schi" or "moof" or "traf")
            {
                var nested = FindBox(data, type, offset + 8, offset + size);
                if (nested.size > 0)
                    return nested;
            }
            offset += size;
        }

        return (0, 0, 0);
    }

    private static byte[] FullBox(string type, byte version, int flags, params byte[][] payloads)
    {
        return Box(type, Concat([(byte)version, (byte)((flags >> 16) & 0xFF), (byte)((flags >> 8) & 0xFF), (byte)(flags & 0xFF)], Concat(payloads)));
    }

    private static byte[] Box(string type, params byte[][] payloads)
    {
        var payload = Concat(payloads);
        var output = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(0, 4), (uint)output.Length);
        FourCc(type).CopyTo(output, 4);
        payload.CopyTo(output, 8);
        return output;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var output = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(output, offset);
            offset += part.Length;
        }
        return output;
    }

    private static byte[] FourCc(string value) => Encoding.ASCII.GetBytes(value);

    private static byte[] UInt16(ushort value)
    {
        var output = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(output, value);
        return output;
    }

    private static byte[] UInt32(uint value)
    {
        var output = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(output, value);
        return output;
    }

    private static byte[] Int32(int value)
    {
        var output = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(output, value);
        return output;
    }
}
