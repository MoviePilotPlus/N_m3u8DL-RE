
# 添加 SRT 字幕格式支持计划

## 问题分析

运行 `dotnet run` 命令处理包含 SRT 字幕的 m3u8 文件时抛出异常 `System.Exception: 错误的m3u8`。

根本原因：
1. `HLSExtractor.cs` 中的 `FetchPlayListAsync` 方法只识别 `.ttml` 和 `.vtt`/`.webvtt` 格式的字幕，没有识别 `.srt` 格式
2. `SimpleDownloadManager.cs` 中的下载管理器有处理 VTT 和 TTML 字幕的代码，但没有处理 SRT 字幕的代码
3. `WebVttSub.cs` 中虽然有 `ToSrt()` 方法，但缺少从 SRT 字符串解析的方法

## 修复方案

### 1. 更新 HLSExtractor.cs
- 在 `FetchPlayListAsync` 方法中添加对 `.srt` 字幕格式的识别
- 当检测到字幕文件 URL 包含 `.srt` 时，设置正确的扩展名

### 2. 更新 WebVttSub.cs
- 添加 `ParseSrt` 静态方法，用于从 SRT 格式字符串解析字幕
- 该方法将把 SRT 格式转换为 WebVttSub 对象，方便后续处理和转换

### 3. 更新 SimpleDownloadManager.cs
- 添加自动修复 SRT raw 字幕的代码块
- 该代码块将：
  - 读取所有 SRT 分片
  - 合并它们并修复时间戳
  - 可选转换为 VTT 或保持 SRT 格式
  - 写出合并后的字幕文件

## 修改的文件列表

1. `/workspace/src/N_m3u8DL-RE.Parser/Extractor/HLSExtractor.cs`
2. `/workspace/src/N_m3u8DL-RE.Common/Entity/WebVttSub.cs`
3. `/workspace/src/N_m3u8DL-RE/DownloadManager/SimpleDownloadManager.cs`

## 实施步骤

1. 修改 HLSExtractor.cs - 添加 SRT 格式识别
2. 修改 WebVttSub.cs - 添加 SRT 解析方法
3. 修改 SimpleDownloadManager.cs - 添加 SRT 字幕修复处理
4. 测试修复是否正常工作

## 风险与注意事项

- SRT 格式解析需要正确处理时间戳格式（使用逗号分隔毫秒）
- 确保与现有的 VTT 和 TTML 处理代码保持一致的风格
- 保持二进制合并功能正常工作

