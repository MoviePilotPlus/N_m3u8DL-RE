using System;
using System.Collections;
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
        using var inputStream = File.OpenRead(inputPath);
        using var outputStream = File.OpenWrite(outputPath);
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
        try
        {
            long inputFileSize = inputStream.Length;
            using var fileReader = new BinaryReader(inputStream);
            using var fileWriter = new BinaryWriter(outputStream);

            int pid1 = -1;
            List<byte> pid1Buffer = new();
            List<int> pid1Offset = new();
            List<byte> pid1PesPayload = new();

            int pid2 = -1;
            List<byte> pid2Buffer = new();
            List<int> pid2Offset = new();
            List<byte> pid2PesPayload = new();

            do
            {
                int loc1 = TS_PACKET_SIZE - 4;
                var packetHeader = fileReader.ReadBytes(4);

                if (packetHeader.Length == 4 && packetHeader[0] == SYNC_BYTE)
                {
                    var packetHeaderBit = new BitArray(packetHeader);
                    int packetPid = (packetHeader[1] & 0x1F) << 8 | packetHeader[2];

                    bool loc3 = false;
                    bool loc4 = false;

                    if (packetHeaderBit[14] && packetPid >= 32 && packetPid <= 1024)
                    {
                        loc3 = true;
                        loc4 = true;

                        if (pid1 < 0)
                        {
                            pid1 = packetPid;
                        }
                        else if (pid2 < 0 && packetPid != pid1)
                        {
                            pid2 = packetPid;
                        }
                    }
                    else
                    {
                        if (packetPid == pid1 || packetPid == pid2)
                        {
                            loc3 = true;
                        }
                    }

                    if (loc3)
                    {
                        var packetData = new byte[TS_PACKET_SIZE];
                        Array.Copy(packetHeader, 0, packetData, 0, packetHeader.Length);

                        if (packetHeaderBit[29])
                        {
                            var loc5 = fileReader.ReadBytes(1);
                            packetData[TS_PACKET_SIZE - loc1] = loc5[0];
                            loc1--;

                            int loc6 = loc5[0];
                            if (loc6 > 0)
                            {
                                loc5 = fileReader.ReadBytes(loc6);
                                Array.Copy(loc5, 0, packetData, TS_PACKET_SIZE - loc1, loc5.Length);
                                loc1 -= loc6;
                            }
                        }

                        int offset = TS_PACKET_SIZE - loc1;
                        var pesData = fileReader.ReadBytes(loc1);
                        Array.Copy(pesData, 0, packetData, offset, pesData.Length);

                        if (loc4)
                        {
                            int loc7 = 9 + pesData[8];
                            var loc8 = new byte[pesData.Length - loc7];
                            Array.Copy(pesData, loc7, loc8, 0, pesData.Length - loc7);
                            offset += loc7;
                            pesData = loc8;
                        }

                        if (packetPid == pid1)
                        {
                            if (loc4) FlushData(fileWriter, ref pid1Buffer, ref pid1Offset, ref pid1PesPayload, key);
                            pid1Offset.Add(offset);
                            pid1Buffer.AddRange(packetData);
                            pid1PesPayload.AddRange(pesData);
                        }
                        else if (packetPid == pid2)
                        {
                            if (loc4) FlushData(fileWriter, ref pid2Buffer, ref pid2Offset, ref pid2PesPayload, key);
                            pid2Offset.Add(offset);
                            pid2Buffer.AddRange(packetData);
                            pid2PesPayload.AddRange(pesData);
                        }
                    }
                    else
                    {
                        fileWriter.Write(packetHeader);
                        fileWriter.Write(fileReader.ReadBytes(loc1));
                    }
                }
            } while (fileReader.BaseStream.Position < inputFileSize - 1);

            FlushData(fileWriter, ref pid1Buffer, ref pid1Offset, ref pid1PesPayload, key);
            FlushData(fileWriter, ref pid2Buffer, ref pid2Offset, ref pid2PesPayload, key);
        }
        catch
        {
            // Ignore
        }
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

    private static void FlushData(BinaryWriter fileWriter, ref List<byte> buffer, ref List<int> offset, ref List<byte> pesPayload, byte[] key)
    {
        try
        {
            if (buffer.Count > 0)
            {
                var decrypted = DecryptES(pesPayload.ToArray(), key);

                int loc2 = 0;
                int loc3 = 0;

                foreach (var loc4 in offset)
                {
                    var loc5 = new byte[TS_PACKET_SIZE];
                    Array.Copy(buffer.ToArray(), loc2, loc5, 0, loc4);
                    loc2 += TS_PACKET_SIZE;
                    Array.Copy(decrypted, loc3, loc5, loc4, TS_PACKET_SIZE - loc4);
                    loc3 += TS_PACKET_SIZE - loc4;
                    fileWriter.Write(loc5);
                }

                buffer.Clear();
                offset.Clear();
                pesPayload.Clear();
            }
        }
        catch
        {
            // Ignore
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
}
