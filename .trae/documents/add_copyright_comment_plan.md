# N_m3u8DL-RE - 添加版权和注释元数据功能实施计划

## 任务分解和优先级

### [x] 任务 1: 在 MyOption 类中添加版权和注释属性
- **优先级**: P0
- **依赖**: 无
- **描述**: 在 `src/N_m3u8DL-RE/CommandLine/MyOption.cs` 文件中添加 `CopyrightInfo` 和 `CommnetInfo` 属性
- **成功标准**: MyOption 类中包含两个新的字符串属性
- **测试要求**:
  - `programmatic`: 编译通过，属性可以正常访问
  - `human-judgement`: 代码风格与现有代码一致

### [x] 任务 2: 在 CommandInvoker 中添加命令行选项
- **优先级**: P0
- **依赖**: 任务 1
- **描述**: 在 `src/N_m3u8DL-RE/CommandLine/CommandInvoker.cs` 中添加 `--copyright-info` 和 `--commnet-info` 选项
- **成功标准**: 命令行解析器能够识别并处理这两个新选项
- **测试要求**:
  - `programmatic`: 命令行选项可以正常解析
  - `human-judgement`: 选项定义与现有选项风格一致

### [x] 任务 3: 在 MergeUtil 中添加版权和注释参数支持
- **优先级**: P0
- **依赖**: 任务 1, 任务 2
- **描述**: 在 `src/N_m3u8DL-RE/Util/MergeUtil.cs` 中的 `MuxInputsByFFmpeg` 方法中添加版权和注释参数支持
- **成功标准**: 方法能够接收并使用版权和注释参数
- **测试要求**:
  - `programmatic`: 方法签名更新，编译通过
  - `human-judgement`: 参数使用与现有代码风格一致

### [x] 任务 4: 在混流调用中传递版权和注释信息
- **优先级**: P0
- **依赖**: 任务 3
- **描述**: 在调用混流方法时，从 MyOption 中获取版权和注释信息并传递给相应方法
- **成功标准**: 版权和注释信息能够正确传递到混流方法
- **测试要求**:
  - `programmatic`: 代码编译通过
  - `human-judgement`: 调用逻辑清晰，与现有代码风格一致

### [x] 任务 5: 测试功能
- **优先级**: P1
- **依赖**: 所有前面的任务
- **描述**: 测试新添加的命令行选项是否能够正确工作
- **成功标准**: 代码编译通过，命令行选项能够被正确解析
- **测试要求**:
  - `programmatic`: 命令行选项能够被正确解析
  - `human-judgement`: 输出视频包含指定的版权和注释信息

## 实施步骤

1. **修改 MyOption.cs**: 添加两个新属性
2. **修改 CommandInvoker.cs**: 添加命令行选项定义和绑定
3. **修改 MergeUtil.cs**: 更新方法签名和实现，添加版权和注释参数支持
4. **修改调用代码**: 在混流调用时传递版权和注释信息
5. **测试验证**: 确保功能正常工作

## 技术细节

- 版权信息通过 `--copyright-info` 选项指定
- 注释信息通过 `--commnet-info` 选项指定
- 这些信息将作为元数据添加到混流后的视频文件中
- 支持的格式包括 MP4、MKV 等
- 实现参考 `/Users/summer/dev/mine/summerandwinter/N_m3u8DL-RE/` 中的代码