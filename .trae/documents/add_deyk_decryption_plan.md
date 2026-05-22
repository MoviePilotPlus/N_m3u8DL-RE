# N_m3u8DL-RE - 添加 DeYK TS 解密方法的实现计划

## 问题分析

用户提供了一个 VB.NET 的 TS 解密代码（PLUGIN_DeYK.vb），需要添加到项目中。用户希望使用现有的 `--custom-hls-method AES_128_YK` 选项来启用该解密。

### VB.NET 代码分析

**解密逻辑：**
1. **DecryptTS 方法**：主入口，读取 TS 数据包（188字节），识别需要解密的 PID
2. **FlushData 方法**：刷新并解密收集的数据
3. **DecryptES 方法**：实际解密操作，使用 AES-128-ECB 模式

**关键特点：**
- 使用 AES-128-ECB 模式
- Key 和 IV 使用同一个密钥
- 不使用填充（PaddingMode.None）
- 在 TS 层识别需要解密的 PID（范围 32-1024）
- 收集 PES 负载数据进行解密
- 解密后重新组装成 TS 包

### 与现有解密方法的对比

| 特性 | VB.NET DeYK | BBTSDecryptionUtil |
|------|-------------|-------------------|
| 加密模式 | AES-ECB | AES-CTR |
| IV 使用 | 与 Key 相同 | 独立 IV |
| 解密位置 | PES 负载 | PES 级别 |
| 识别方式 | PID 范围 32-1024 | 特定 PID |

结论：**项目中没有相同的解密方法，需要添加。**

## 任务分解和优先级

### [ ] 任务 1: 在 EncryptMethod 枚举中添加 AES_128_YK
- **优先级**: P0
- **依赖**: 无
- **描述**: 在 EncryptMethod 枚举中添加 AES_128_YK 值
- **成功标准**: 枚举更新完成
- **测试要求**:
  - `programmatic`: 代码编译通过

### [ ] 任务 2: 创建 DeYKDecryptionUtil 类
- **优先级**: P0
- **依赖**: 任务 1
- **描述**: 将 VB.NET 代码转换为 C#，创建 DeYKDecryptionUtil 解密工具类
- **成功标准**: 创建完整的解密工具类
- **测试要求**:
  - `programmatic`: 代码编译通过
  - `human-judgement`: 代码结构清晰，符合项目编码规范

### [ ] 任务 3: 添加解密方法到下载流程
- **优先级**: P1
- **依赖**: 任务 1, 2
- **描述**: 在下载管理器中添加对 AES_128_YK 解密的支持
- **成功标准**: 下载流程能够调用新的解密方法
- **测试要求**:
  - `programmatic`: 代码编译通过
  - `human-judgement`: 集成逻辑正确

### [ ] 任务 4: 测试和验证
- **优先级**: P1
- **依赖**: 任务 1, 2, 3
- **描述**: 测试解密功能是否正常工作
- **成功标准**: 能够成功解密 TS 文件
- **测试要求**:
  - `programmatic`: 解密流程没有错误
  - `human-judgement`: 解密后的文件可以正常播放

## 实施步骤

1. **更新 EncryptMethod 枚举**：添加 AES_128_YK 值
2. **创建 DeYKDecryptionUtil.cs**：实现 AES-ECB TS 解密逻辑
3. **集成到下载流程**：在 SimpleDownloadManager 中添加对 AES_128_YK 解密的调用

## 代码修改位置

1. **`src/N_m3u8DL-RE.Common/Enum/EncryptMethod.cs`**
   - 添加 AES_128_YK 枚举值

2. **`src/N_m3u8DL-RE/Crypto/DeYKDecryptionUtil.cs`**（新建）
   - 实现 DeYK 解密逻辑

3. **`src/N_m3u8DL-RE/DownloadManager/SimpleDownloadManager.cs`**
   - 添加对 AES_128_YK 解密的调用

## 技术细节

### AES-ECB 解密
```csharp
private static byte[] DecryptES(byte[] inputStream, byte[] key)
{
    using var aes = Aes.Create();
    aes.BlockSize = 128;
    aes.KeySize = 128;
    aes.Key = key;
    aes.IV = key; // IV 与 Key 相同
    aes.Mode = CipherMode.ECB;
    aes.Padding = PaddingMode.None;
    
    var decryptor = aes.CreateDecryptor();
    var length = inputStream.Length - (inputStream.Length % 16);
    var decrypted = decryptor.TransformFinalBlock(inputStream, 0, length);
    Array.Copy(decrypted, inputStream, decrypted.Length);
    return inputStream;
}
```

### TS 包处理
- TS 包大小：188 字节
- 同步字节：0x47
- PID 范围：32-1024 需要解密
- PES 负载解密后重新组装

## 注意事项

- 保持与现有代码的兼容性
- 使用项目现有的加密工具类结构
- 处理网络错误和超时情况
- 添加适当的日志记录