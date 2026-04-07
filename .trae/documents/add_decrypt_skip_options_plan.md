# N_m3u8DL-RE - 添加控制忽略字幕流和音频流解密的命令行参数实施计划

## 任务分解和优先级

### [x] 任务 1: 在 MyOption 类中添加跳过解密的属性
- **优先级**: P0
- **依赖**: 无
- **描述**: 在 `src/N_m3u8DL-RE/CommandLine/MyOption.cs` 文件中添加 `SkipSubtitleDecrypt` 和 `SkipAudioDecrypt` 布尔属性
- **成功标准**: MyOption 类中包含两个新的布尔属性
- **测试要求**:
  - `programmatic`: 编译通过，属性可以正常访问
  - `human-judgement`: 代码风格与现有代码一致

### [x] 任务 2: 在 CommandInvoker 中添加命令行选项
- **优先级**: P0
- **依赖**: 任务 1
- **描述**: 在 `src/N_m3u8DL-RE/CommandLine/CommandInvoker.cs` 中添加 `--skip-subtitle-decrypt` 和 `--skip-audio-decrypt` 选项
- **成功标准**: 命令行解析器能够识别并处理这两个新选项
- **测试要求**:
  - `programmatic`: 命令行选项可以正常解析
  - `human-judgement`: 选项定义与现有选项风格一致

### [x] 任务 3: 在下载和解密逻辑中添加跳过解密的判断
- **优先级**: P0
- **依赖**: 任务 1, 任务 2
- **描述**: 在 `src/N_m3u8DL-RE/DownloadManager/SimpleDownloadManager.cs` 的 `DownloadStreamAsync` 方法中，根据流的类型和新选项决定是否跳过解密
- **成功标准**: 字幕流和音频流的解密可以根据命令行选项被跳过
- **测试要求**:
  - `programmatic`: 代码编译通过，逻辑正确
  - `human-judgement`: 代码逻辑清晰，与现有代码风格一致

### [x] 任务 4: 测试功能
- **优先级**: P1
- **依赖**: 所有前面的任务
- **描述**: 测试新添加的命令行选项是否能够正确工作
- **成功标准**: 代码编译通过，命令行选项能够被正确解析
- **测试要求**:
  - `programmatic`: 命令行选项能够被正确解析
  - `human-judgement`: 功能按照预期工作

## 实施步骤

1. **修改 MyOption.cs**: 添加两个新的布尔属性
2. **修改 CommandInvoker.cs**: 添加命令行选项定义和绑定
3. **修改 SimpleDownloadManager.cs**: 在解密逻辑中添加跳过判断
4. **测试验证**: 确保功能正常工作

## 技术细节

- 跳过字幕流解密通过 `--skip-subtitle-decrypt` 选项指定
- 跳过音频流解密通过 `--skip-audio-decrypt` 选项指定
- 这些选项会影响实时解密和合并后解密的逻辑
- 实现逻辑：在解密前检查流的类型和对应选项，决定是否跳过解密