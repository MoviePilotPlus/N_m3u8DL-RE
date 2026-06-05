using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Crypto;

/// <summary>
/// DeYK TS 解密工具类
/// 参考 PLUGIN_DeYK.vb 实现
/// </summary>
internal static class DeYKDecryptionUtil
{
    private const int TS_PACKET_SIZE = 188;
    private const byte SYNC_BYTE = 0x47;
    private const int MinElementaryPid = 32;
    private const int MaxElementaryPid = 1024;

    /// <summary>
    /// 解密 TS 文件（流式处理，支持大文件）
    /// </summary>
    /// <param name="inputPath">输入文件路径</param>
    /// <param name="outputPath">输出文件路径</param>
    /// <param name="keyHex">AES 密钥（hex格式）</param>
    public static void DecryptFile(string inputPath, string outputPath, string keyHex)
    {
        var keyBytes = HexUtil.HexToBytes(keyHex);
        DecryptFile(inputPath, outputPath, keyBytes);
    }

    /// <summary>
    /// 解密 TS 文件（流式处理，支持大文件）
    /// </summary>
    /// <param name="inputPath">输入文件路径</param>
    /// <param name="outputPath">输出文件路径</param>
    /// <param name="keyBytes">AES 密钥（16字节）</param>
    public static void DecryptFile(string inputPath, string outputPath, byte[] keyBytes)
    {
        ValidateKey(keyBytes);

        using var inputStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        DecryptTS(inputStream, outputStream, keyBytes);
    }

    /// <summary>
    /// 解密 TS 数据流（流式处理）
    /// </summary>
    /// <param name="inputStream">输入流</param>
    /// <param name="outputStream">输出流</param>
    /// <param name="key">AES 密钥（16字节）</param>
    public static void DecryptTS(Stream inputStream, Stream outputStream, byte[] key)
    {
        ValidateKey(key);

        using var fileReader = new BinaryReader(inputStream, System.Text.Encoding.UTF8, leaveOpen: true);
        using var fileWriter = new BinaryWriter(outputStream, System.Text.Encoding.UTF8, leaveOpen: true);

        var pid1 = new PidDecryptState();
        var pid2 = new PidDecryptState();

        while (true)
        {
            var packet = fileReader.ReadBytes(TS_PACKET_SIZE);
            if (packet.Length == 0) break;

            if (packet.Length != TS_PACKET_SIZE || packet[0] != SYNC_BYTE)
            {
                fileWriter.Write(packet);
                CopyRemaining(inputStream, outputStream);
                break;
            }

            var packetPid = GetPid(packet);
            var payloadStart = HasPayloadUnitStartIndicator(packet);
            var startsTargetPes = payloadStart && packetPid is >= MinElementaryPid and <= MaxElementaryPid;
            var shouldDecrypt = startsTargetPes || packetPid == pid1.Pid || packetPid == pid2.Pid;

            if (!shouldDecrypt)
            {
                fileWriter.Write(packet);
                continue;
            }

            if (startsTargetPes)
            {
                if (pid1.Pid < 0)
                {
                    pid1.Pid = packetPid;
                }
                else if (pid2.Pid < 0 && packetPid != pid1.Pid)
                {
                    pid2.Pid = packetPid;
                }
            }

            var state = packetPid == pid1.Pid ? pid1 : packetPid == pid2.Pid ? pid2 : null;
            if (state == null)
            {
                fileWriter.Write(packet);
                continue;
            }

            if (!TryGetPayloadOffset(packet, out var payloadOffset))
            {
                fileWriter.Write(packet);
                continue;
            }

            if (startsTargetPes)
            {
                FlushData(fileWriter, state, key);

                if (!TrySkipPesHeader(packet, payloadOffset, out var esOffset))
                {
                    fileWriter.Write(packet);
                    continue;
                }

                payloadOffset = esOffset;
            }

            state.PayloadOffsets.Add(payloadOffset);
            state.PacketBuffer.AddRange(packet);
            state.PesPayload.AddRange(packet[payloadOffset..]);
        }

        FlushData(fileWriter, pid1, key);
        FlushData(fileWriter, pid2, key);
    }

    /// <summary>
    /// 解密 TS 数据流（保留接口，用于向后兼容）
    /// </summary>
    /// <param name="inputStream">输入数据</param>
    /// <param name="key">AES 密钥（16字节）</param>
    /// <returns>解密后的数据</returns>
    public static byte[] DecryptTS(byte[] inputStream, byte[] key)
    {
        try
        {
            using var msInput = new MemoryStream(inputStream);
            using var msOutput = new MemoryStream();
            DecryptTS(msInput, msOutput, key);
            return msOutput.ToArray();
        }
        catch
        {
            return inputStream;
        }
    }

    public static bool IsLikelyTsFile(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length < TS_PACKET_SIZE) return false;

        var packetsToCheck = Math.Min(5, stream.Length / TS_PACKET_SIZE);
        for (var i = 0; i < packetsToCheck; i++)
        {
            stream.Position = i * TS_PACKET_SIZE;
            if (stream.ReadByte() != SYNC_BYTE) return false;
        }

        return true;
    }

    private static void ValidateKey(byte[] key)
    {
        if (key.Length != 16)
        {
            throw new ArgumentException("AES_128_YK key must be 16 bytes.", nameof(key));
        }
    }

    private static int GetPid(byte[] packet)
    {
        return (packet[1] & 0x1F) << 8 | packet[2];
    }

    private static bool HasPayloadUnitStartIndicator(byte[] packet)
    {
        return (packet[1] & 0x40) != 0;
    }

    private static bool HasAdaptationField(byte[] packet)
    {
        return (packet[3] & 0x20) != 0;
    }

    private static bool HasPayload(byte[] packet)
    {
        return (packet[3] & 0x10) != 0;
    }

    private static bool TryGetPayloadOffset(byte[] packet, out int offset)
    {
        offset = 4;

        if (!HasPayload(packet))
        {
            offset = TS_PACKET_SIZE;
            return true;
        }

        if (!HasAdaptationField(packet)) return true;

        offset = 5 + packet[4];
        return offset <= TS_PACKET_SIZE;
    }

    private static bool TrySkipPesHeader(byte[] packet, int payloadOffset, out int esOffset)
    {
        esOffset = payloadOffset;
        var pesHeaderDataLengthOffset = payloadOffset + 8;

        if (pesHeaderDataLengthOffset >= TS_PACKET_SIZE) return false;

        esOffset = payloadOffset + 9 + packet[pesHeaderDataLengthOffset];
        return esOffset <= TS_PACKET_SIZE;
    }

    private static void CopyRemaining(Stream inputStream, Stream outputStream)
    {
        inputStream.CopyTo(outputStream);
    }

    private static void FlushData(BinaryWriter fileWriter, PidDecryptState state, byte[] key)
    {
        if (state.PacketBuffer.Count <= 0) return;

        try
        {
            var decrypted = DecryptES(state.PesPayload.ToArray(), key);
            var buffer = state.PacketBuffer.ToArray();

            var packetPosition = 0;
            var payloadPosition = 0;

            foreach (var payloadOffset in state.PayloadOffsets)
            {
                var packet = new byte[TS_PACKET_SIZE];
                Array.Copy(buffer, packetPosition, packet, 0, payloadOffset);

                var payloadLength = TS_PACKET_SIZE - payloadOffset;
                Array.Copy(decrypted, payloadPosition, packet, payloadOffset, payloadLength);

                packetPosition += TS_PACKET_SIZE;
                payloadPosition += payloadLength;

                fileWriter.Write(packet);
            }
        }
        catch
        {
            fileWriter.Write(state.PacketBuffer.ToArray());
        }
        finally
        {
            state.Clear();
        }
    }

    private static byte[] DecryptES(byte[] inputStream, byte[] key)
    {
        using var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.KeySize = 128;
        aes.Key = key;
        aes.IV = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        var decryptor = aes.CreateDecryptor();
        int loc3 = inputStream.Length - (inputStream.Length % 16);
        var loc4 = decryptor.TransformFinalBlock(inputStream, 0, loc3);
        Array.Copy(loc4, inputStream, loc4.Length);
        return inputStream;
    }

    private sealed class PidDecryptState
    {
        public int Pid { get; set; } = -1;
        public List<byte> PacketBuffer { get; } = new();
        public List<int> PayloadOffsets { get; } = new();
        public List<byte> PesPayload { get; } = new();

        public void Clear()
        {
            PacketBuffer.Clear();
            PayloadOffsets.Clear();
            PesPayload.Clear();
        }
    }
}
