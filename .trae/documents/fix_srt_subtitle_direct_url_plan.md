# 修复 SRT 字幕直接 URL 问题计划

## 问题分析

当 m3u8 文件中的字幕 URI 直接指向 .srt 文件而不是 m3u8 文件时，程序会抛出 "错误的m3u8" 异常。

根本原因：
1. `HLSExtractor.FetchPlayListAsync` 方法对所有流（包括字幕流）都调用 `LoadM3u8FromUrlAsync`
2. `LoadM3u8FromUrlAsync` 会读取文件内容并调用 `PreProcessContent`
3. `PreProcessContent` 检查内容是否以 `#EXTM3U` 开头
4. 由于 SRT 文件内容不是以 `#EXTM3U` 开头，会抛出异常

## 修复方案

### 1. 修改 FetchPlayListAsync 方法
- 在调用 `LoadM3u8FromUrlAsync` 之前，检查字幕流的 URL 是否指向 .srt 文件
- 如果是 SRT 文件，直接创建一个包含单个 segment 的 playlist，而不是尝试解析为 m3u8

### 2. 实现 SRT 字幕直接 URL 处理逻辑
- 对于 SRT 直接 URL，创建一个新的 MediaSegment
- 设置 segment 的 URL 为 SRT 文件 URL
- 创建一个 MediaPart 包含这个 segment
- 创建一个 Playlist 包含这个 MediaPart
- 设置 streamSpec 的 Playlist 和 Extension

## 修改的文件

1. `/workspace/src/N_m3u8DL-RE.Parser/Extractor/HLSExtractor.cs`

## 实施步骤

1. 修改 `FetchPlayListAsync` 方法，添加对 SRT 直接 URL 的检测
2. 实现 SRT 直接 URL 的处理逻辑
3. 测试修复是否正常工作

## 风险与注意事项

- 确保只对字幕流的 SRT URL 进行特殊处理
- 保持与现有 VTT 和 TTML 处理逻辑的一致性
- 确保创建的 Playlist 结构正确，以便后续处理能够正常工作
