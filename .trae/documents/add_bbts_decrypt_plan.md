# BBTS视频解密功能集成计划

## 1. 项目研究结论

### BBTS加密格式分析
BBTS（Broadband Transport Stream）是爱奇艺使用的专有视频加密格式，主要特点：

1. **加密方式**: AES-128-ECB
2. **IV来源**: 从TS流的SDT（Service Description Table）中提取，格式为 `mdcm|...|...|<IV>`
3. **M3U8标识**: `#EXT-X-KEY:METHOD=SAMPLE-AES,URI="skd://...",KEYFORMAT="com.iqiyi.bbts"`

### 解密流程
1. 解析TS包（188字节固定长度）
2. 从SDT（PID=0x0011）中提取IV
3. 从PMT（PID=0x1000）中获取流信息（视频/音频PID和类型）
4. 对PES（Packetized Elementary Stream）数据进行解密
5. 使用CTR模式变种解密（每16字节递增计数器）
6. 处理emulation prevention bytes (0x00 0x00 0x03 → 0x00 0x00)

### 当前项目架构
- `EncryptMethod` 枚举定义加密方式
- `EncryptInfo` 存储加密信息（Key, IV, Method）
- `SimpleDownloader` 处理不同加密方式的解密
- `AESUtil` 提供AES解密功能
- `DefaultHLSKeyProcessor` 处理HLS的KEY标签

## 2. 需要修改的文件

### 新增文件
| 文件路径 | 说明 |
|---------|------|
| `src/N_m3u8DL-RE/Crypto/BBTSDecryptUtil.cs` | BBTS解密核心工具类 |

### 修改文件
| 文件路径 | 修改内容 |
|---------|---------|
| `src/N_m3u8DL-RE.Common/Enum/EncryptMethod.cs` | 添加 `BBTS` 枚举值 |
| `src/N_m3u8DL-RE/Downloader/SimpleDownloader.cs` | 添加BBTS解密分支处理 |
| `src/N_m3u8DL-RE.Parser/Processor/HLS/DefaultHLSKeyProcessor.cs` | 识别 `com.iqiyi.bbts` KEYFORMAT |

## 3. 实现步骤

### 步骤1: 添加BBTS加密方法枚举
在 `EncryptMethod.cs` 中添加：
```csharp
public enum EncryptMethod
{
    NONE,
    AES_128,
    AES_128_ECB,
    SAMPLE_AES,
    SAMPLE_AES_CTR,
    CENC,
    CHACHA20,
    BBTS,    // 新增
    UNKNOWN
}
```

### 步骤2: 创建BBTS解密工具类
创建 `BBTSDecryptUtil.cs`，实现以下核心功能：

1. **常量定义**
   - TS包大小: 188字节
   - 同步字节: 0x47
   - 各种PID: PAT(0x0000), SDT(0x0011), PMT(0x1000)

2. **TS包解析**
   - `TsPID()` - 提取PID
   - `TsPUSI()` - 检查PUSI标志
   - `TsAFC()` - 获取适配字段控制
   - `TsPayloadOffset()` - 计算负载偏移

3. **PSI表解析**
   - `PSIAssembler` - PSI段重组器
   - `ParseSDTAndExtractIV()` - 从SDT提取IV
   - `ParsePMTStreams()` - 从PMT提取流信息

4. **PES解密**
   - `DecryptESSparseWithEmulationRemoval()` - ES流解密（处理emulation prevention bytes）
   - `DecryptPESNormal()` - PES包解密
   - `CtrInc()` - CTR计数器递增

5. **主解密入口**
   - `DecryptBBTSFile()` - 解密BBTS文件到TS文件

### 步骤3: 修改HLS Key处理器
在 `DefaultHLSKeyProcessor.cs` 中：
- 检测 `KEYFORMAT="com.iqiyi.bbts"`
- 将METHOD设置为 `EncryptMethod.BBTS`
- 从URI中提取或保留Key

### 步骤4: 修改下载器
在 `SimpleDownloader.cs` 中添加BBTS解密分支：
```csharp
case EncryptMethod.BBTS:
{
    var key = segment.EncryptInfo.Key;
    BBTSDecryptUtil.DecryptBBTSFile(dResult.ActualFilePath, des, key!);
    break;
}
```

## 4. 技术细节

### TS包结构
```
| 同步字节 | PID | PUSI | AFC | 负载 |
|   1B    | 2B  | 1b   | 2b  | 变长 |
```

### SDT解析
SDT包含服务描述信息，BBTS的IV隐藏在服务名称（descriptor tag 0x48）中：
- 格式: `mdcm|<part1>|<part2>|<IV_hex>`
- IV为16字节，但只使用前12字节

### PES解密流程
1. 保留PES头部（9 + header_data_length字节）
2. 保留NAL头（H.264为4字节，其他可能不同）
3. 对ES数据进行CTR模式解密
4. 处理emulation prevention bytes

### CTR模式变种
- 初始IV来自SDT
- 每16字节递增计数器
- 使用AES-ECB加密计数器后与数据异或

## 5. 潜在依赖和注意事项

1. **性能考虑**: BBTS解密需要逐包处理，对于大文件可能较慢
2. **内存管理**: 需要合理处理大文件的流式解密
3. **错误处理**: 需要处理无效TS包、缺失SDT/PMT等异常情况
4. **兼容性**: 确保不影响现有加密方式的解密

## 6. 风险处理

| 风险 | 处理方案 |
|-----|---------|
| IV提取失败 | 提供手动指定IV的选项 |
| Key格式错误 | 验证Key长度（16字节） |
| 非标准BBTS格式 | 添加详细日志便于调试 |
| 性能问题 | 考虑并行处理优化 |

## 7. 测试建议

1. 使用爱奇艺BBTS加密视频进行测试
2. 验证解密后TS文件可正常播放
3. 测试边界情况（空文件、损坏文件等）
4. 性能测试（大文件解密时间）
