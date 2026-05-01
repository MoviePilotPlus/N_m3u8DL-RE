# 爱奇艺 BBTS 加密视频解密集成计划

## 1. 概述

本计划将爱奇艺 bbts 加密视频的解密功能集成到 N_m3u8DL-RE 项目中。原始参考实现位于 `/Users/summer/dev/mine/summerandwinter/bbtsdecrypt/bbts.go`（Go 语言）。

## 2. 现有代码分析

### 2.1 bbts.go 核心功能

bbts.go 提供了完整的 bbts 加密视频解密功能：

- **TS 数据包解析**：提取加密流的 PAT、PMT、SDT 等表信息
- **AES-CTR 解密**：实现自定义的 AES-CTR 解密算法
- **IV 提取**：从 SDT 表中的服务描述信息中提取 IV
- **PES 解析**：解析视频/音频 PES 包
- **原始流恢复**：移除加密信息，输出标准 TS 文件

### 2.2 N_m3u8DL-RE 项目结构

- **加密相关枚举**：`EncryptMethod.cs`（N_m3u8DL-RE.Common 项目）
- **解密工具类**：`AESUtil.cs`、`MP4DecryptUtil.cs`（N_m3u8DL-RE 项目）
- **下载管理器**：`SimpleDownloadManager.cs`（负责下载和解密）
- **HLS 解析器**：`HLSExtractor.cs`、`DefaultHLSKeyProcessor.cs`
- **加密信息类**：`EncryptInfo.cs`

## 3. 集成方案

### 3.1 核心修改点

1. **扩展 EncryptMethod 枚举**，增加 BBTS 类型
2. **创建新的解密工具类** BBTSEncryptionUtil，实现 bbts 解密逻辑
3. **集成到下载流程**，确保下载后调用解密工具处理 bbts 文件
4. **扩展 m3u8 识别**，检测并标记 bbts 加密的内容

### 3.2 详细实现步骤

#### 步骤 1：扩展 EncryptMethod 枚举
- 位置：`/Users/summer/dev/github/MoviePilotPlus/N_m3u8DL-RE/src/N_m3u8DL-RE.Common/Enum/EncryptMethod.cs`
- 添加 `BBTS` 类型

#### 步骤 2：创建 BBTS 解密工具类
- 位置：`/Users/summer/dev/github/MoviePilotPlus/N_m3u8DL-RE/src/N_m3u8DL-RE/Crypto/BBTSDecryptionUtil.cs`
- 实现内容：
  - TS 数据包解析
  - SDT 表解析和 IV 提取
  - PES 数据包解析和解密
  - AES-CTR 解密逻辑
  - 解密文件保存

#### 步骤 3：修改下载管理器
- 位置：`/Users/summer/dev/github/MoviePilotPlus/N_m3u8DL-RE/src/N_m3u8DL-RE/DownloadManager/SimpleDownloadManager.cs`
- 集成 bbts 解密逻辑，在下载分片后检测并解密 bbts 文件

#### 步骤 4：扩展 HLS 提取器（可选）
- 位置：`/Users/summer/dev/github/MoviePilotPlus/N_m3u8DL-RE/src/N_m3u8DL-RE.Parser/Extractor/HLSExtractor.cs`
- 可在解析时检测 bbts URL 特征（.bbts 后缀），并标记加密类型

## 4. 技术细节

### 4.1 bbts 加密流程（来自 bbts.go）

1. **加密标记**：SDT 表服务描述包含 "mdcm|" 字符串和 IV
2. **IV 格式**：`mdcm|<未知>|<未知>|<IV>`
3. **加密算法**：AES-CTR 模式，有特殊的处理逻辑
4. **加密范围**：主要加密视频 PES 数据部分

### 4.2 集成的关键点

- 密钥通过程序参数提供（与现有自定义密钥流程兼容）
- 解密在下载后、合并前进行
- 保持与现有其他解密流程的一致性

## 5. 文件清单

| 操作 | 文件路径 | 描述 |
|------|----------|------|
| 修改 | `src/N_m3u8DL-RE.Common/Enum/EncryptMethod.cs` | 添加 BBTS 加密类型 |
| 新建 | `src/N_m3u8DL-RE/Crypto/BBTSDecryptionUtil.cs` | bbts 解密核心实现 |
| 修改 | `src/N_m3u8DL-RE/DownloadManager/SimpleDownloadManager.cs` | 集成 bbts 解密到下载流程 |

## 6. 风险和注意事项

1. **跨语言翻译**：将 Go 代码正确翻译为 C#，注意字节处理、数组索引等细节
2. **测试验证**：需要使用爱奇艺 bbts 加密视频进行完整测试
3. **兼容性**：确保不破坏现有其他加密类型的功能

## 7. 实施进度

1. 代码分析和设计 ✅（本计划）
2. 核心解密工具实现
3. 集成到下载管理器
4. 测试和调试
5. 完成
