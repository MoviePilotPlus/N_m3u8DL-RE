using CSChaCha20;

namespace N_m3u8DL_RE.Crypto;

internal static class ChaCha20Util
{
    private const int TsPacketSize = 188;
    private const byte TsSyncByte = 0x47;

    /// <summary>
    /// 按固定 188 字节间隔抽样检查 TS 同步字节，判断文件是否已是明文 TS。
    /// 用于拦截"命令行注入 CHACHA20 但部分子播放列表本身未加密"的场景
    /// （如腾讯 HiFi 音轨无 EXT-X-KEY 行），避免把明文当密文解坏。
    /// </summary>
    public static bool IsLikelyPlainTs(byte[] buffer)
    {
        if (buffer.Length < TsPacketSize) return false;

        var packetsToCheck = Math.Min(6, buffer.Length / TsPacketSize);
        for (var i = 0; i < packetsToCheck; i++)
        {
            if (buffer[i * TsPacketSize] != TsSyncByte) return false;
        }

        return true;
    }

    public static byte[] DecryptPer1024Bytes(byte[] encryptedBuff, byte[] keyBytes, byte[] nonceBytes)
    {
        if (keyBytes.Length != 32)
            throw new Exception("Key must be 32 bytes!");
        if (nonceBytes.Length != 12 && nonceBytes.Length != 8)
            throw new Exception("Nonce must be 12 or 8 bytes!");
        if (nonceBytes.Length == 8)
            nonceBytes = (new byte[4] { 0, 0, 0, 0 }).Concat(nonceBytes).ToArray();

        var decStream = new MemoryStream();
        using BinaryReader reader = new BinaryReader(new MemoryStream(encryptedBuff));
        using (BinaryWriter writer = new BinaryWriter(decStream))
            while (true)
            {
                var buffer = reader.ReadBytes(1024);
                byte[] dec = new byte[buffer.Length];
                if (buffer.Length > 0)
                {
                    ChaCha20 forDecrypting = new ChaCha20(keyBytes, nonceBytes, 0);
                    forDecrypting.DecryptBytes(dec, buffer);
                    writer.Write(dec, 0, dec.Length);
                }
                else
                {
                    break;
                }
            }

        return decStream.ToArray();
    }
}