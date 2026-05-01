using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Crypto;

/// <summary>
/// 爱奇艺 BBTS 加密视频解密工具类
/// 参考 bbts.go 实现
/// </summary>
internal static class BBTSDecryptionUtil
{
    private const int TS_PACKET_SIZE = 188;
    private const byte SYNC_BYTE = 0x47;
    private const ushort PID_PAT = 0x0000;
    private const ushort PID_SDT = 0x0011;
    private const ushort PID_PMT = 0x1000;
    private const ushort VIDEO_PID = 0x0100;
    private const ushort AUDIO_PID = 0x0101;
    private const int IV_COPY_LEN = 12;

    private static void CtrInc(byte[] counter)
    {
        var c = 1;
        for (var i = 15; i >= 0; i--)
        {
            c += counter[i];
            counter[i] = (byte)(c & 0xFF);
            c >>= 8;
            if (c == 0) break;
        }
    }

    private static ushort TsPID(byte[] pkt)
    {
        return (ushort)((ushort)(pkt[1] & 0x1F) << 8 | (ushort)pkt[2]);
    }

    private static bool TsPUSI(byte[] pkt)
    {
        return (pkt[1] & 0x40) != 0;
    }

    private static int TsAFC(byte[] pkt)
    {
        return (pkt[3] >> 4) & 0x3;
    }

    private static bool TsHasPayload(byte[] pkt)
    {
        var afc = TsAFC(pkt);
        return afc == 1 || afc == 3;
    }

    private static int TsPayloadOffset(byte[] pkt)
    {
        var afc = TsAFC(pkt);
        return afc switch
        {
            1 => 4,
            3 => 5 + pkt[4],
            _ => TS_PACKET_SIZE
        };
    }

    private static bool IVEquals(byte[] a, byte[] b)
    {
        if (a.Length != 16 || b.Length != 16) return false;
        for (var i = 0; i < 16; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    private static bool ParseSDTAndSetIV(byte[] section, byte[] ivec)
    {
        if (section.Length < 16 || section[0] != 0x42) return false;

        var sectionLength = ((ushort)(section[1] & 0x0F) << 8 | (ushort)section[2]);
        var end = 3 + sectionLength;
        if (end > section.Length) return false;

        var pos = 3 + 8;
        while (pos + 5 <= end - 4)
        {
            var descLoopLen = ((ushort)(section[pos + 3] & 0x0F) << 8 | (ushort)section[pos + 4]);
            var dpos = pos + 5;
            var dend = dpos + descLoopLen;

            while (dpos + 2 <= dend && dpos + 2 <= end - 4)
            {
                var tag = section[dpos];
                var length = section[dpos + 1];
                dpos += 2;
                if (dpos + length > section.Length) break;

                var body = section.Skip(dpos).Take(length).ToArray();
                dpos += length;

                if (tag == 0x48 && body.Length >= 3)
                {
                    var providerLen = body[1];
                    if (2 + providerLen >= body.Length) continue;

                    var snLenIdx = 2 + providerLen;
                    var snLen = body[snLenIdx];
                    var serviceNameBytes = body.Skip(snLenIdx + 1).Take(snLen).ToArray();
                    var serviceName = Encoding.UTF8.GetString(serviceNameBytes);

                    if (!serviceName.Contains("mdcm|")) continue;

                    var parts = serviceName.Split('|');
                    if (parts.Length < 4) continue;

                    var ivHex = parts[3];
                    if (string.IsNullOrEmpty(ivHex)) continue;
                    ivHex = ivHex.Substring(1);

                    if (!HexUtil.TryParseHexString(ivHex, out var ivBin) || ivBin is not { Length: > 0 }) continue;

                    for (var i = 0; i < 16; i++) ivec[i] = 0;
                    for (var i = 0; i < IV_COPY_LEN && i < ivBin.Length; i++) ivec[i] = ivBin[i];

                    return true;
                }
            }

            pos = dend;
        }

        return false;
    }

    private static List<StreamInfo> ParsePMTStreams(byte[] section)
    {
        var streams = new List<StreamInfo>();

        if (section.Length < 12 || section[0] != 0x02) return streams;

        var sectionLength = ((ushort)(section[1] & 0x0F) << 8 | (ushort)section[2]);
        var end = 3 + sectionLength;
        if (end > section.Length) return streams;

        var programInfoLen = ((ushort)(section[10] & 0x0F) << 8 | (ushort)section[11]);
        var pos = 12 + programInfoLen;

        while (pos + 5 <= end - 4)
        {
            var st = section[pos];
            var pid = (ushort)((ushort)(section[pos + 1] & 0x1F) << 8 | (ushort)section[pos + 2]);
            var esInfoLen = ((ushort)(section[pos + 3] & 0x0F) << 8 | (ushort)section[pos + 4]);
            streams.Add(new StreamInfo { PID = pid, StreamType = st });
            pos += 5 + esInfoLen;
        }

        return streams;
    }

    private static byte FindStreamType(List<StreamInfo> streams, ushort pid)
    {
        foreach (var s in streams)
        {
            if (s.PID == pid) return s.StreamType;
        }
        return 0;
    }

    private static void DecryptESSparseWithEmulationRemoval(byte[] es, ICryptoTransform block, byte[] ivStart)
    {
        var newES = new List<byte>();
        var i = 0;
        while (i < es.Length)
        {
            if (i + 2 < es.Length && es[i] == 0 && es[i + 1] == 0 && es[i + 2] == 3)
            {
                newES.Add(0);
                newES.Add(0);
                i += 3;
            }
            else
            {
                newES.Add(es[i]);
                i++;
            }
        }

        var iv = new byte[16];
        Buffer.BlockCopy(ivStart, 0, iv, 0, 16);

        var esLen = newES.Count;
        var pos = 0;
        var counter = 0;

        while (esLen > 0)
        {
            CtrInc(iv);
            var tmp = new byte[16];
            Buffer.BlockCopy(iv, 0, tmp, 0, 16);

            if (esLen <= 16 || counter % 10 == 0)
            {
                block.TransformBlock(tmp, 0, 16, tmp, 0);
            }

            var decLen = 16;
            if (esLen < 16) decLen = esLen;

            for (var k = 0; k < decLen; k++)
            {
                newES[pos + k] ^= tmp[k];
            }

            esLen -= decLen;
            pos += 16;
            counter++;
        }

        if (newES.Count != es.Length)
        {
            var diff = es.Length - newES.Count;
            if (diff > 0)
            {
                newES.AddRange(es.Skip(es.Length - diff));
            }
        }

        Buffer.BlockCopy(newES.ToArray(), 0, es, 0, es.Length);
    }

    private static void DecryptPESNormal(byte[] pes, byte streamType, ICryptoTransform block, byte[] ivSnap)
    {
        if (pes.Length < 9) return;

        var pesHeaderLen = pes[8];
        var headerEnd = 9 + pesHeaderLen;
        if (headerEnd > pes.Length) return;

        var newPES = new List<byte>();
        newPES.AddRange(pes.Take(headerEnd));

        var nalHdrLen = 1;
        if (streamType != 0x1B) nalHdrLen = 2;

        var posSt = headerEnd;
        var j = posSt;

        while (j < pes.Length)
        {
            if (j == pes.Length - 1)
            {
                if (pes.Length - 2 > posSt + 3 + nalHdrLen)
                {
                    newPES.AddRange(pes.Skip(posSt).Take(3 + nalHdrLen));
                    var es = pes.Skip(posSt + 3 + nalHdrLen).Take(pes.Length - 2 - (posSt + 3 + nalHdrLen)).ToArray();
                    var esCopy = new byte[es.Length];
                    Buffer.BlockCopy(es, 0, esCopy, 0, es.Length);
                    if (esCopy.Length > 0)
                    {
                        DecryptESSparseWithEmulationRemoval(esCopy, block, ivSnap);
                    }
                    newPES.AddRange(esCopy);
                    newPES.AddRange(pes.Skip(pes.Length - 2));
                }
                else
                {
                    newPES.AddRange(pes.Skip(posSt));
                }
            }
            else
            {
                if (j + 2 < pes.Length && pes[j] == 0 && pes[j + 1] == 0 && pes[j + 2] == 1)
                {
                    if (j != posSt)
                    {
                        if (j - 2 > posSt + 3 + nalHdrLen)
                        {
                            newPES.AddRange(pes.Skip(posSt).Take(3 + nalHdrLen));

                            var es = new List<byte>();
                            var flag = false;
                            if (pes[j - 1] == 0)
                            {
                                flag = true;
                                es.AddRange(pes.Skip(posSt + 3 + nalHdrLen).Take(j - 3 - (posSt + 3 + nalHdrLen)));
                            }
                            else
                            {
                                es.AddRange(pes.Skip(posSt + 3 + nalHdrLen).Take(j - 2 - (posSt + 3 + nalHdrLen)));
                            }

                            if (es.Count > 0)
                            {
                                var esArr = es.ToArray();
                                DecryptESSparseWithEmulationRemoval(esArr, block, ivSnap);
                                newPES.AddRange(esArr);
                            }

                            if (flag)
                            {
                                newPES.AddRange(pes.Skip(j - 3).Take(3));
                            }
                            else
                            {
                                newPES.AddRange(pes.Skip(j - 2).Take(2));
                            }
                        }
                        else
                        {
                            newPES.AddRange(pes.Skip(posSt).Take(j - posSt));
                        }
                        posSt = j;
                    }
                }
            }
            j++;
        }

        Buffer.BlockCopy(newPES.ToArray(), 0, pes, 0, newPES.Count);
    }

    private static int ReadAllBytes(Stream stream, byte[] buffer, int offset, int count)
    {
        var totalBytesRead = 0;
        var bytesLeft = count;
        while (totalBytesRead < count)
        {
            var bytesRead = stream.Read(buffer, offset + totalBytesRead, bytesLeft);
            if (bytesRead == 0) break;
            totalBytesRead += bytesRead;
            bytesLeft -= bytesRead;
        }
        return totalBytesRead;
    }

    /// <summary>
    /// 解密 BBTS 文件为标准 TS 文件
    /// </summary>
    /// <param name="inputPath">输入的 bbts 文件路径</param>
    /// <param name="outputPath">输出的 ts 文件路径</param>
    /// <param name="keyHex">AES 密钥（16字节，hex格式）</param>
    public static void DecryptFile(string inputPath, string outputPath, string keyHex)
    {
        if (!HexUtil.TryParseHexString(keyHex, out var keyBytes))
            throw new ArgumentException("Invalid hex key");
        if (keyBytes == null)
            throw new ArgumentException("Invalid hex key");
        DecryptFile(inputPath, outputPath, keyBytes);
    }

    /// <summary>
    /// 解密 BBTS 文件为标准 TS 文件
    /// </summary>
    /// <param name="inputPath">输入的 bbts 文件路径</param>
    /// <param name="outputPath">输出的 ts 文件路径</param>
    /// <param name="keyBytes">AES 密钥（16字节）</param>
    public static void DecryptFile(string inputPath, string outputPath, byte[] keyBytes)
    {
        if (keyBytes == null)
            throw new ArgumentNullException(nameof(keyBytes));
        using var inputStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
        using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        DecryptStream(inputStream, outputStream, keyBytes);
    }

    /// <summary>
    /// 解密 BBTS 流为标准 TS 流
    /// </summary>
    /// <param name="inputStream">输入流</param>
    /// <param name="outputStream">输出流</param>
    /// <param name="keyBytes">AES 密钥</param>
    public static void DecryptStream(Stream inputStream, Stream outputStream, byte[] keyBytes)
    {
        var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        var encryptor = aes.CreateEncryptor();

        var state = new EncryptionState
        {
            AesECBUser = encryptor,
            IV = new byte[16],
            Ready = false
        };

        List<StreamInfo> pmtStreams = new List<StreamInfo>();
        var sdtAsm = new PSIAssembler();
        var pmtAsm = new PSIAssembler();

        List<byte>? pes = null;
        List<PESHeaderChunk>? pesHeaders = null;
        ushort lastPID = 0xFFFF;
        byte[]? ivSnapForPES = null;

        void FlushPES()
        {
            if (pes == null || pes.Count == 0 || pesHeaders == null || pesHeaders.Count == 0 || !state.Ready)
            {
                pes = null;
                pesHeaders = null;
                ivSnapForPES = null;
                lastPID = 0xFFFF;
                return;
            }

            var sidPrev = (byte)0xE1;
            if (pes.Count > 3) sidPrev = pes[3];

            if (sidPrev == 0xE0 && pes.Count > 8 && ivSnapForPES != null)
            {
                var streamType = FindStreamType(pmtStreams, lastPID);
                var pesArr = pes.ToArray();
                DecryptPESNormal(pesArr, streamType, state.AesECBUser, ivSnapForPES);
                pes.Clear();
                pes.AddRange(pesArr);
            }

            var pesRemain = pes.Count;
            var pesPos = 0;

            for (var i = 0; i < pesHeaders.Count; i++)
            {
                var h = pesHeaders[i];
                var payloadCap = TS_PACKET_SIZE - h.HeaderSize;

                byte[] payload;
                if (pesRemain <= 0)
                {
                    payload = new byte[payloadCap];
                    Array.Fill(payload, (byte)0xFF);
                }
                else if (pesRemain < payloadCap)
                {
                    if (i == pesHeaders.Count - 1)
                    {
                        var hdr = new byte[h.HeaderSize];
                        Buffer.BlockCopy(h.HeaderBytes, 0, hdr, 0, h.HeaderSize);

                        var stuffingNeeded = payloadCap - pesRemain;
                        var afc = (hdr[3] >> 4) & 0x3;

                        if (afc == 1)
                        {
                            hdr[3] = (byte)((hdr[3] & 0x0F) | 0x30);
                            var newHdr = new List<byte>(hdr);
                            if (stuffingNeeded == 1)
                            {
                                newHdr.Add(0x00);
                            }
                            else
                            {
                                newHdr.Add((byte)(stuffingNeeded - 1));
                                newHdr.Add(0x00);
                                for (var s = 0; s < stuffingNeeded - 2; s++)
                                {
                                    newHdr.Add(0xFF);
                                }
                            }

                            outputStream.Write(newHdr.ToArray(), 0, newHdr.Count);
                            outputStream.Write(pes.Skip(pesPos).Take(pesRemain).ToArray(), 0, pesRemain);
                            pesPos += pesRemain;
                            pesRemain = 0;
                            continue;
                        }
                        else if (afc == 3)
                        {
                            var afLen = hdr[4];
                            var extra = stuffingNeeded;
                            hdr[4] = (byte)(afLen + extra);

                            var newHdr = new List<byte>();
                            newHdr.AddRange(hdr.Take(5 + afLen));
                            for (var s = 0; s < extra; s++)
                            {
                                newHdr.Add(0xFF);
                            }
                            newHdr.AddRange(hdr.Skip(5 + afLen));

                            outputStream.Write(newHdr.ToArray(), 0, newHdr.Count);
                            outputStream.Write(pes.Skip(pesPos).Take(pesRemain).ToArray(), 0, pesRemain);
                            pesPos += pesRemain;
                            pesRemain = 0;
                            continue;
                        }
                    }

                    payload = pes.Skip(pesPos).Take(pesRemain).ToArray();
                    pesPos += pesRemain;
                    pesRemain = 0;
                }
                else
                {
                    payload = pes.Skip(pesPos).Take(payloadCap).ToArray();
                    pesPos += payloadCap;
                    pesRemain -= payloadCap;
                }

                outputStream.Write(h.HeaderBytes, 0, h.HeaderBytes.Length);
                outputStream.Write(payload, 0, payload.Length);
            }

            pes = null;
            pesHeaders = null;
            ivSnapForPES = null;
            lastPID = 0xFFFF;
        }

        var buf = new byte[TS_PACKET_SIZE];

        while (true)
        {
            var readResult = ReadAllBytes(inputStream, buf, 0, TS_PACKET_SIZE);
            if (readResult < TS_PACKET_SIZE)
            {
                FlushPES();
                break;
            }

            var pkt = new byte[TS_PACKET_SIZE];
            Buffer.BlockCopy(buf, 0, pkt, 0, TS_PACKET_SIZE);

            if (pkt[0] != SYNC_BYTE)
            {
                outputStream.Write(pkt, 0, pkt.Length);
                continue;
            }

            var pid = TsPID(pkt);

            if (pid == PID_PAT)
            {
                FlushPES();
                outputStream.Write(pkt, 0, pkt.Length);
                continue;
            }

            if (pid == PID_SDT)
            {
                FlushPES();
                var sec = sdtAsm.Push(pkt);
                if (sec != null)
                {
                    var newIVec = new byte[16];
                    if (ParseSDTAndSetIV(sec, newIVec))
                    {
                        if (!IVEquals(state.IV, newIVec))
                        {
                            Buffer.BlockCopy(newIVec, 0, state.IV, 0, 16);
                            pmtStreams.Clear();
                        }
                        state.Ready = true;
                    }
                }
                outputStream.Write(pkt, 0, pkt.Length);
                continue;
            }

            if (pid == PID_PMT)
            {
                FlushPES();
                var sec = pmtAsm.Push(pkt);
                if (sec != null && state.Ready)
                {
                    pmtStreams = ParsePMTStreams(sec);
                }
                outputStream.Write(pkt, 0, pkt.Length);
                continue;
            }

            if (!state.Ready)
            {
                outputStream.Write(pkt, 0, pkt.Length);
                continue;
            }

            var interceptPIDs = new Dictionary<ushort, bool>
            {
                [VIDEO_PID] = true,
                [AUDIO_PID] = true
            };

            if (!interceptPIDs.ContainsKey(pid))
            {
                FlushPES();
                outputStream.Write(pkt, 0, pkt.Length);
                continue;
            }

            if (!TsHasPayload(pkt))
            {
                FlushPES();
                outputStream.Write(pkt, 0, pkt.Length);
                continue;
            }

            var off = TsPayloadOffset(pkt);
            if (off >= TS_PACKET_SIZE)
            {
                FlushPES();
                outputStream.Write(pkt, 0, pkt.Length);
                continue;
            }

            var isNewPES = false;
            if (off + 8 < TS_PACKET_SIZE && pkt[off] == 0x00 && pkt[off + 1] == 0x00 && pkt[off + 2] == 0x01)
            {
                var sid = pkt[off + 3];
                if (sid == 0xC0 || sid == 0xE0)
                {
                    isNewPES = true;
                }
            }

            if (isNewPES && pes != null && pes.Count > 0)
            {
                FlushPES();
            }

            if (!isNewPES && (pes == null || pes.Count == 0))
            {
                outputStream.Write(pkt, 0, pkt.Length);
                continue;
            }

            if (isNewPES)
            {
                ivSnapForPES = new byte[16];
                Buffer.BlockCopy(state.IV, 0, ivSnapForPES, 0, 16);
            }

            if (pes == null) pes = new List<byte>();
            if (pesHeaders == null) pesHeaders = new List<PESHeaderChunk>();

            if (TsAFC(pkt) == 3)
            {
                pes.AddRange(pkt.Skip(off));
                pesHeaders.Add(new PESHeaderChunk
                {
                    HeaderBytes = pkt.Take(off).ToArray(),
                    HeaderSize = off
                });
            }
            else
            {
                pes.AddRange(pkt.Skip(4));
                pesHeaders.Add(new PESHeaderChunk
                {
                    HeaderBytes = pkt.Take(4).ToArray(),
                    HeaderSize = 4
                });
            }

            lastPID = pid;
        }
    }

    private class EncryptionState
    {
        public required ICryptoTransform AesECBUser { get; set; }
        public required byte[] IV { get; set; }
        public bool Ready { get; set; }
    }

    private class PESHeaderChunk
    {
        public required byte[] HeaderBytes { get; set; }
        public int HeaderSize { get; set; }
    }

    private class StreamInfo
    {
        public ushort PID { get; set; }
        public byte StreamType { get; set; }
    }

    private class PSIAssembler
    {
        private readonly List<byte> _buffer = new List<byte>();
        private int? _expectedTotal;
        private bool _collecting;

        public byte[]? Push(byte[] pkt)
        {
            if (pkt.Length != TS_PACKET_SIZE || !TsHasPayload(pkt)) return null;

            var off = TsPayloadOffset(pkt);
            if (off >= TS_PACKET_SIZE) return null;

            var payload = pkt.Skip(off).ToArray();

            if (TsPUSI(pkt))
            {
                var pointer = payload[0];
                payload = payload.Skip(1).ToArray();
                if (pointer > payload.Length) return null;
                payload = payload.Skip(pointer).ToArray();
                _buffer.Clear();
                _expectedTotal = null;
                _collecting = true;
            }

            if (!_collecting) return null;

            _buffer.AddRange(payload);

            if (_expectedTotal == null && _buffer.Count >= 3)
            {
                var sectionLength = ((ushort)(_buffer[1] & 0x0F) << 8 | (ushort)_buffer[2]);
                _expectedTotal = 3 + sectionLength;
            }

            if (_expectedTotal != null && _buffer.Count >= _expectedTotal.Value)
            {
                var section = _buffer.Take(_expectedTotal.Value).ToArray();
                _buffer.Clear();
                _expectedTotal = null;
                _collecting = false;
                return section;
            }

            return null;
        }
    }
}
