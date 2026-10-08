using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Entity;
using Spectre.Console;
using System.Diagnostics;
using System.Text;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Common.Entity;
using System.Globalization;
using System.Net.Sockets;

namespace N_m3u8DL_RE.Util;

internal static class MergeUtil
{
    /// <summary>
    /// 输入一堆已存在的文件，合并到新文件
    /// </summary>
    /// <param name="files"></param>
    /// <param name="outputFilePath"></param>
    public static void CombineMultipleFilesIntoSingleFile(string[] files, string outputFilePath)
    {
        if (files.Length == 0) return;
        if (files.Length == 1)
        {
            FileInfo fi = new FileInfo(files[0]);
            fi.CopyTo(outputFilePath, true);
            return;
        }

        if (!Directory.Exists(Path.GetDirectoryName(outputFilePath)))
            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);

        var inputFilePaths = files;
        using var outputStream = File.Create(outputFilePath);
        foreach (var inputFilePath in inputFilePaths)
        {
            if (inputFilePath == "")
                continue;
            using var inputStream = File.OpenRead(inputFilePath);
            inputStream.CopyTo(outputStream);
        }
    }

    private static int InvokeFFmpeg(string binary, string command, string workingDirectory)
    {
        return InvokeFFmpeg(binary, command, workingDirectory, out _);
    }

    private static int InvokeFFmpeg(string binary, string command, string workingDirectory, out string errorOutput, bool loopbackInput = false)
    {
        Logger.DebugMarkUp($"{binary}: {command}");

        // 收集 ffmpeg 的 stderr 输出，便于后续判断失败原因（如句柄耗尽）
        var errorBuilder = new StringBuilder();
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo()
        {
            WorkingDirectory = workingDirectory,
            FileName = binary,
            Arguments = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (loopbackInput)
        {
            // concat 内部打开 HTTP 时不会继承 http_proxy 参数，只对当前子进程
            // 增补回环地址的代理豁免，不能修改整个程序的代理环境。
            p.StartInfo.Environment.TryGetValue("no_proxy", out var noProxy);
            p.StartInfo.Environment["no_proxy"] = string.IsNullOrEmpty(noProxy) ? "127.0.0.1" : $"{noProxy},127.0.0.1";
        }
        p.ErrorDataReceived += (sendProcess, output) =>
        {
            if (!string.IsNullOrEmpty(output.Data))
            {
                errorBuilder.AppendLine(output.Data);
                Logger.WarnMarkUp($"[grey]{output.Data.EscapeMarkup()}[/]");
            }
        };
        p.Start();
        p.BeginErrorReadLine();
        p.WaitForExit();
        errorOutput = errorBuilder.ToString();
        return p.ExitCode;
    }

    /// <summary>
    /// 判断 ffmpeg 的输出是否为文件句柄耗尽（Too many open files）导致的错误。
    /// concat 协议会一次性打开全部分片，分片过多 + 系统句柄上限过低时会触发该错误。
    /// </summary>
    internal static bool IsTooManyOpenFilesError(string ffmpegOutput)
    {
        return !string.IsNullOrEmpty(ffmpegOutput)
            && ffmpegOutput.Contains("too many open files", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 分块合并的分片数量阈值。
    /// </summary>
    internal const int PartialMergeThreshold = 1800;

    /// <summary>
    /// 判断合并前是否需要先做分块合并(<see cref="PartialCombineMultipleFiles"/>)。
    /// 分块合并只为 concat 协议服务: 该模式会一次性打开全部分片(见 #338、#89), 且所有文件名都要放在命令行上。
    /// concat demuxer 通过临时清单文件逐个读取分片, 上述两个限制都不存在;
    /// 而分块合并会把上百个分片按字节直接拼进单个 TS 中间文件, ffmpeg 只能把它当作一条连续流读取,
    /// 无法处理文件内部的时间戳重置, 导致时间轴错乱、时长严重偏短(见 #946)。
    /// 本机虚拟输入也没有上述两个限制，只在用户显式选择直接 concat 协议时保留分块合并。
    /// </summary>
    internal static bool ShouldPartialMerge(int fileCount, FFmpegConcatMode mode)
    {
        return fileCount >= PartialMergeThreshold && mode == FFmpegConcatMode.PROTOCOL;
    }

    internal static string BuildPartsConcatList(string[] files, IReadOnlyList<MediaPart> parts)
    {
        if (files.Length != parts.Count)
            throw new ArgumentException(ResString.mediaPartInputMismatch);
        var text = new StringBuilder("ffconcat version 1.0\n");
        double elapsed = 0;
        for (var i = 0; i < files.Length; i++)
        {
            var path = Path.GetFullPath(files[i]);
            if (path.Contains('\n') || path.Contains('\r'))
                throw new ArgumentException(ResString.concatInputPathInvalid);
            text.Append("file '").Append(path.Replace("'", "'\\''")).Append("'\n");
            var part = parts[i];
            var start = part.OutputStart ?? elapsed;
            var duration = part.OutputDuration ?? part.MediaSegments.Sum(s => s.Duration);
            var nextStart = i + 1 < parts.Count ? parts[i + 1].OutputStart ?? start + duration : start + duration;
            // DASH 的 PTO 是源媒体的时间原点。inpoint/outpoint 保留音视频各自的
            // 起始偏移并裁掉跨越 Period 尾部的分片；duration 统一推进各轨道的时间轴。
            if (part.PeriodIndex != null)
            {
                var inpoint = part.OutputInpoint ?? Math.Max(part.PresentationTimeOffset ?? 0,
                    part.MediaSegments.FirstOrDefault()?.PresentationTime ?? 0);
                text.Append("inpoint ").Append(inpoint.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                text.Append("outpoint ").Append((inpoint + duration).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }
            text.Append("duration ").Append((nextStart - start).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            elapsed = start + duration;
        }
        return text.ToString();
    }

    public static bool ConcatMediaParts(string binary, string[] files, IReadOnlyList<MediaPart> parts, string output)
    {
        // init+媒体先形成各自可读取的文件；concat demuxer 重新读取每份配置，
        // 避免直接按字节拼接多个 moov 和发生回退的 tfdt。
        var listPath = Path.GetTempFileName();
        var normalized = new List<string>();
        try
        {
            var inputs = files.ToArray();
            for (var i = 0; i < inputs.Length; i++)
            {
                if (Path.GetExtension(inputs[i]).ToLowerInvariant() is not (".mp4" or ".m4a" or ".m4s"))
                    continue;
                // concat demuxer 要求各输入轨道 time_base 一致。init 的 timescale 可以
                // 不同，FFmpeg 解密也会改写它，因此先无损 remux 到共同的视频 timescale。
                // copyts 保留 PTO 对应的源时间，不能在此把每份输入单独归零。
                var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputs[i]))!, $"{Guid.NewGuid():N}.normalized.mp4");
                normalized.Add(path);
                if (InvokeFFmpeg(binary,
                    $"-loglevel warning -nostdin -y -copyts -avoid_negative_ts disabled -i \"{Path.GetFullPath(inputs[i])}\" -map 0:v? -map 0:a? -map 0:s? -c copy -video_track_timescale 90000 \"{path}\"",
                    Path.GetDirectoryName(path)!) != 0)
                    return false;
                inputs[i] = path;
            }
            File.WriteAllText(listPath, BuildPartsConcatList(inputs, parts), new UTF8Encoding(false));
            var start = parts.FirstOrDefault()?.OutputStart ?? 0;
            var offset = start == 0 ? "" : $" -itsoffset {start.ToString("R", CultureInfo.InvariantCulture)}";
            var copyTs = parts.Any(p => p.OutputStart != null) ? " -copyts -avoid_negative_ts disabled" : "";
            // MPEG-TS 默认额外延迟输出时钟，字幕公共时间轴已经归零，必须保留该时间轴。
            var tsOptions = copyTs.Length > 0 && Path.GetExtension(output).Equals(".ts", StringComparison.OrdinalIgnoreCase)
                ? " -mpegts_copyts 1 -muxdelay 0" : "";
            return InvokeFFmpeg(binary,
                $"-loglevel warning -nostdin -y{copyTs}{offset} -f concat -safe 0 -i \"{listPath}\" -map 0:v? -map 0:a? -map 0:s? -c copy{tsOptions} \"{Path.GetFullPath(output)}\"",
                Path.GetDirectoryName(Path.GetFullPath(files[0]))!) == 0;
        }
        finally
        {
            File.Delete(listPath);
            foreach (var path in normalized) File.Delete(path);
        }
    }

    public static string[] PartialCombineMultipleFiles(string[] files)
    {
        var newFiles = new List<string>();
        var div = files.Length <= 90000 ? 100 : 200;

        var outputName = Path.Combine(Path.GetDirectoryName(files[0])!, "T");
        var index = 0; // 序号

        // 按照div的容量分割为小数组
        var li = Enumerable.Range(0, files.Length / div + 1).Select(x => files.Skip(x * div).Take(div).ToArray()).ToArray();
        foreach (var items in li)
        {
            if (items.Length == 0)
                continue;
            var output = outputName + index.ToString("0000") + ".ts";
            CombineMultipleFilesIntoSingleFile(items, output);
            newFiles.Add(output);
            // 合并后删除这些文件
            foreach (var item in items)
            {
                File.Delete(item);
            }
            index++;
        }

        return newFiles.ToArray();
    }

    public static bool MergeByFFmpeg(string binary, string[] files, string outputPath, string muxFormat, bool useAACFilter,
        bool fastStart = false,
        bool writeDate = true, FFmpegConcatMode concatMode = FFmpegConcatMode.LOCAL_HTTP, string poster = "", string audioName = "", string title = "",
        string copyright = "", string comment = "", string encodingTool = "", string recTime = "")
    {
        // 改为绝对路径
        outputPath = Path.GetFullPath(outputPath);

        string dateString = string.IsNullOrEmpty(recTime) ? DateTime.Now.ToString("o") : recTime;

        string ddpAudio = string.Empty;
        string addPoster = "-map 1 -c:v:1 copy -disposition:v:1 attached_pic";
        ddpAudio = (File.Exists($"{Path.GetFileNameWithoutExtension(outputPath + ".mp4")}.txt") ? File.ReadAllText($"{Path.GetFileNameWithoutExtension(outputPath + ".mp4")}.txt") : "");
        if (!string.IsNullOrEmpty(ddpAudio)) useAACFilter = false;

        ConcatInputServer? concatInputServer = null;
        string? listPath = null;
        if (concatMode == FFmpegConcatMode.LOCAL_HTTP)
        {
            try
            {
                concatInputServer = new ConcatInputServer(files);
            }
            catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException)
            {
                Logger.WarnMarkUp(string.Format(ResString.ffmpegConcatInputFailed, ex.Message).EscapeMarkup());
                return false;
            }
        }

        // 三种模式只改变输入构造，转封装参数保持一致。
        string BuildCommand()
        {
            StringBuilder command = new StringBuilder("-loglevel warning -nostdin ");
            if (concatMode == FFmpegConcatMode.DEMUXER)
            {
                // 使用 concat demuxer合并
                var text = string.Join(Environment.NewLine, files.Select(f => $"file '{f}'"));
                listPath = Path.GetTempFileName();
                File.WriteAllText(listPath, text);
                command.Append($" -f concat -safe 0 -i \"{listPath}");
            }
            else if (concatInputServer != null)
            {
                // 仍交给 concat 协议读取连续字节，但只打开一个可 seek 的虚拟资源。
                command.Append($" -protocol_whitelist concat,http,tcp -i \"concat:{concatInputServer.Url}");
            }
            else
            {
                command.Append(" -i concat:\"");
                foreach (string t in files)
                {
                    command.Append(Path.GetFileName(t) + "|");
                }
            }


            switch (muxFormat.ToUpper())
            {
                case ("MP4"):
                    command.Append("\" " + (string.IsNullOrEmpty(poster) ? "" : "-i \"" + poster + "\""));
                    command.Append(" " + (string.IsNullOrEmpty(ddpAudio) ? "" : "-i \"" + ddpAudio + "\""));
                    command.Append(
                        $" -map 0:v? {(string.IsNullOrEmpty(ddpAudio) ? "-map 0:a?" : $"-map {(string.IsNullOrEmpty(poster) ? "1" : "2")}:a -map 0:a?")} -map 0:s? " + (string.IsNullOrEmpty(poster) ? "" : addPoster)
                        + (writeDate ? " -metadata date=\"" + dateString + "\"" : "") +
                        " -metadata encoding_tool=\"" + encodingTool + "\" -metadata title=\"" + title +
                        "\" -metadata copyright=\"" + copyright + "\" -metadata comment=\"" + comment +
                        $"\" -metadata:s:a:{(string.IsNullOrEmpty(ddpAudio) ? "0" : "1")} title=\"" + audioName + $"\" -metadata:s:a:{(string.IsNullOrEmpty(ddpAudio) ? "0" : "1")} handler=\"" + audioName + "\" ");
                    command.Append(string.IsNullOrEmpty(ddpAudio) ? "" : " -metadata:s:a:0 title=\"DD+\" -metadata:s:a:0 handler=\"DD+\" ");
                    if (fastStart)
                        command.Append("-movflags +faststart");
                    command.Append("  -c copy -y " + (useAACFilter ? "-bsf:a aac_adtstoasc" : "") + " \"" + outputPath + ".mp4\"");
                    break;
                case ("MKV"):
                    command.Append("\" -map 0  -c copy -y " + (useAACFilter ? "-bsf:a aac_adtstoasc" : "") + " \"" + outputPath + ".mkv\"");
                    break;
                case ("FLV"):
                    command.Append("\" -map 0  -c copy -y " + (useAACFilter ? "-bsf:a aac_adtstoasc" : "") + " \"" + outputPath + ".flv\"");
                    break;
                case ("M4A"):
                    command.Append("\" -map 0  -c copy -f mp4 -y " + (useAACFilter ? "-bsf:a aac_adtstoasc" : "") + " \"" + outputPath + ".m4a\"");
                    break;
                case ("TS"):
                    command.Append("\" -map 0  -c copy -y -f mpegts -bsf:v h264_mp4toannexb \"" + outputPath + ".ts\"");
                    break;
                case ("EAC3"):
                    command.Append("\" -map 0:a -c copy -y \"" + outputPath + ".eac3\"");
                    break;
                case ("AAC"):
                    command.Append("\" -map 0:a -c copy -y \"" + outputPath + ".m4a\"");
                    break;
                case ("AC3"):
                    command.Append("\" -map 0:a -c copy -y \"" + outputPath + ".ac3\"");
                    break;
            }

            return command.ToString();
        }

        try
        {
            var workingDirectory = Path.GetDirectoryName(files[0])!;
            var code = InvokeFFmpeg(binary, BuildCommand(), workingDirectory, out var errorOutput, loopbackInput: concatInputServer != null);
            if (concatInputServer?.Error is { } error)
            {
                Logger.WarnMarkUp(string.Format(ResString.ffmpegConcatInputFailed, error.Message).EscapeMarkup());
                return false;
            }
            // 直接 concat 协议仍可能耗尽句柄；失败时不改变时间轴处理方式，也不删除分片。
            if (code != 0 && IsTooManyOpenFilesError(errorOutput))
                Logger.WarnMarkUp(ResString.ffmpegMergeReachLimit);
            return code == 0;
        }
        finally
        {
            concatInputServer?.Dispose();
            if (listPath != null)
                File.Delete(listPath);
        }
    }

    public static bool MuxInputsByFFmpeg(string binary, OutputFile[] files, string outputPath, MuxFormat muxFormat, bool dateinfo, string copyright = "", string comment = "", string? metadataFile = null)
    {
        var ext = OtherUtil.GetMuxExtension(muxFormat);
        string dateString = DateTime.Now.ToString("o");
        StringBuilder command = new StringBuilder("-loglevel warning -nostdin -y -dn ");
        if (files.Any(file => file.PreserveTimestamp))
            command.Append("-copyts ");

        // INPUT
        foreach (var item in files)
        {
            command.Append($" -i \"{item.FilePath}\" ");
        }

        // 如果有元数据文件（FFMETADATA，含章节），在 MAP 之前作为额外输入添加
        int metadataInputIndex = -1;
        if (!string.IsNullOrEmpty(metadataFile) && File.Exists(metadataFile))
        {
            command.Append($" -i \"{metadataFile}\" ");
            metadataInputIndex = files.Length;
        }

        // MAP
        // "-map {i}" pulls in every stream an input has, including ones the mux
        // format can't hold. Some sites' segments carry a data stream alongside
        // the real track (e.g. HLS timed_id3 metadata), and Matroska/MP4 reject
        // the whole mux for it ("Only audio, video, and subtitles are supported"),
        // even with -ignore_unknown set below.
        //
        // Map by stream type instead, not by whole input - and ask for all three
        // types from every input rather than switching on each file's declared
        // MediaType. A "video" input isn't always video-only: some sites hand
        // out one combined file per rendition (audio muxed into the same
        // stream), and restricting that input to just ":v?" would silently
        // drop its audio. ":v?/:a?/:s?" are no-ops on a type a given input
        // doesn't have, so this only ever adds streams, never mismatches one.
        for (int i = 0; i < files.Length; i++)
        {
            command.Append($" -map {i}:v? -map {i}:a? -map {i}:s? ");
        }

        var srt = files.Any(x => x.FilePath.EndsWith(".srt"));

        if (muxFormat == MuxFormat.MP4)
            command.Append($" -strict unofficial -c:a copy -c:v copy -c:s mov_text "); // mp4不支持vtt/srt字幕，必须转换格式
        else if (muxFormat == MuxFormat.TS)
            command.Append($" -strict unofficial -c:a copy -c:v copy ");
        else if (muxFormat == MuxFormat.MKV)
            command.Append($" -strict unofficial -c:a copy -c:v copy -c:s {(srt ? "srt" : "webvtt")} ");
        else throw new ArgumentException($"unknown format: {muxFormat}");

        // CLEAN / METADATA
        if (metadataInputIndex >= 0)
            command.Append($" -map_metadata {metadataInputIndex} ");
        else
            command.Append(" -map_metadata -1 ");

        // LANG and NAME
        var streamIndex = 0;
        for (int i = 0; i < files.Length; i++)
        {
            // 转换语言代码
            LanguageCodeUtil.ConvertLangCodeAndDisplayName(files[i]);
            command.Append($" -metadata:s:{streamIndex} language=\"{files[i].LangCode ?? "und"}\" ");
            if (!string.IsNullOrEmpty(files[i].Description))
            {
                command.Append($" -metadata:s:{streamIndex} title=\"{files[i].Description}\" ");
            }
            /**
             * -metadata:s:xx标记的是 输出的第xx个流的metadata，
             * 若输入文件存在不止一个流时，这里单纯使用files的index
             * 就有可能出现metadata错位的情况，所以加了如下逻辑
             */
            if (files[i].Mediainfos.Count > 0)
                streamIndex += files[i].Mediainfos.Count;
            else
                streamIndex++;
        }

        var videoTracks = files.Where(x => x.MediaType != Common.Enum.MediaType.AUDIO && x.MediaType != Common.Enum.MediaType.SUBTITLES);
        var audioTracks = files.Where(x => x.MediaType == Common.Enum.MediaType.AUDIO);
        var subTracks = files.Where(x => x.MediaType == Common.Enum.MediaType.AUDIO);
        if (videoTracks.Any()) command.Append(" -disposition:v:0 default ");
        // 字幕都不设置默认
        if (subTracks.Any()) command.Append(" -disposition:s 0 ");
        if (audioTracks.Any())
        {
            // 音频除了第一个音轨 都不设置默认
            command.Append(" -disposition:a:0 default ");
            for (int i = 1; i < audioTracks.Count(); i++)
            {
                command.Append($" -disposition:a:{i} 0 ");
            }
        }

        if (dateinfo) command.Append($" -metadata date=\"{dateString}\" ");
        if (!string.IsNullOrEmpty(copyright)) command.Append($" -metadata copyright=\"{copyright}\" ");
        if (!string.IsNullOrEmpty(comment)) command.Append($" -metadata comment=\"{comment}\" ");
        command.Append($" -ignore_unknown -copy_unknown ");
        command.Append($" \"{outputPath}{ext}\"");

        var code = InvokeFFmpeg(binary, command.ToString(), Environment.CurrentDirectory);

        return code == 0;
    }

    public static bool FFmpegSupportsEac3(string binary)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo()
            {
                FileName = binary,
                Arguments = "-hide_banner -codecs",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            p.Start();
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.Contains(" eac3 ", StringComparison.OrdinalIgnoreCase) ||
                             line.Contains(" eac3\t", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Logger.DebugMarkUp($"[grey]Failed to check ffmpeg eac3 support: {ex.Message.EscapeMarkup()}[/]");
            return false;
        }
    }

    public static bool MuxInputsByFFmpegWithFallback(string binary, OutputFile[] files, string outputPath, MuxFormat muxFormat, bool dateinfo, out OutputFile[] muxedInputs, string copyright = "", string comment = "", string? metadataFile = null)
    {
        var ffmpegSupportsEac3 = FFmpegSupportsEac3(binary);
        muxedInputs = FilterFfmpegMp4UnsupportedInputs(binary, files, muxFormat, ffmpegSupportsEac3, out var skippedInputs);
        foreach (var skipped in skippedInputs)
        {
            var reason = ffmpegSupportsEac3
                ? "Current ffmpeg cannot mux this EAC3 audio into MP4"
                : "Current ffmpeg does not report EAC3 codec support";
            Logger.WarnMarkUp($"[yellow]{reason}. Keep audio outside MP4 mux: {Path.GetFileName(skipped.FilePath).EscapeMarkup()}[/]");
        }

        var result = MuxInputsByFFmpeg(binary, muxedInputs, outputPath, muxFormat, dateinfo, copyright, comment, metadataFile);
        if (result || muxFormat != MuxFormat.MP4 || !ffmpegSupportsEac3)
            return result;

        var retryInputs = FilterFfmpegMp4UnsupportedInputs(binary, files, muxFormat, ffmpegSupportsEac3: false, out var retrySkippedInputs);
        if (retrySkippedInputs.Length == 0 || retryInputs.Length == muxedInputs.Length)
            return false;

        Logger.WarnMarkUp("[yellow]ffmpeg reports EAC3 support, but MP4 mux failed. Retrying without EAC3 audio.[/]");
        foreach (var skipped in retrySkippedInputs)
        {
            Logger.WarnMarkUp($"[yellow]Keep audio outside MP4 mux: {Path.GetFileName(skipped.FilePath).EscapeMarkup()}[/]");
        }

        var failedOutput = outputPath + OtherUtil.GetMuxExtension(muxFormat);
        if (File.Exists(failedOutput))
            File.Delete(failedOutput);

        result = MuxInputsByFFmpeg(binary, retryInputs, outputPath, muxFormat, dateinfo, copyright, comment, metadataFile);
        if (result)
            muxedInputs = retryInputs;

        return result;
    }

    public static OutputFile[] FilterFfmpegMp4UnsupportedInputs(string binary, OutputFile[] files, MuxFormat muxFormat, bool ffmpegSupportsEac3, out OutputFile[] skipped)
    {
        skipped = [];
        if (muxFormat != MuxFormat.MP4)
            return files;

        var audioFiles = files.Where(x => x.MediaType == Common.Enum.MediaType.AUDIO).ToArray();
        var eac3Files = audioFiles.Where(IsEac3Audio).ToArray();
        if (eac3Files.Length == 0)
            return files;

        skipped = eac3Files
            .Where(file => !ffmpegSupportsEac3 || !FFmpegCanMuxEac3ToMp4(binary, file))
            .ToArray();
        if (skipped.Length == 0 || skipped.Length == files.Length)
        {
            skipped = [];
            return files;
        }

        var skippedSet = skipped.ToHashSet();
        return files.Where(x => !skippedSet.Contains(x)).ToArray();
    }

    public static bool FFmpegCanMuxEac3ToMp4(string binary, OutputFile file)
    {
        if (!File.Exists(file.FilePath))
            return false;

        var probeOutput = Path.Combine(Path.GetTempPath(), $"n_m3u8dl_re_eac3_probe_{Guid.NewGuid():N}.mp4");
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo()
            {
                FileName = binary,
                Arguments = $"-loglevel error -nostdin -y -i \"{file.FilePath}\" -t 180 -map 0:a:0 -c copy -strict unofficial -f mp4 \"{probeOutput}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            p.Start();
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30000))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                Logger.DebugMarkUp($"[grey]ffmpeg EAC3 MP4 mux probe timed out: {Path.GetFileName(file.FilePath).EscapeMarkup()}[/]");
                return false;
            }

            var output = stdoutTask.GetAwaiter().GetResult() + stderrTask.GetAwaiter().GetResult();
            if (p.ExitCode != 0)
                Logger.DebugMarkUp($"[grey]ffmpeg EAC3 MP4 mux probe failed for {Path.GetFileName(file.FilePath).EscapeMarkup()}: {output.Trim().EscapeMarkup()}[/]");

            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.DebugMarkUp($"[grey]Failed to probe ffmpeg EAC3 MP4 mux support: {ex.Message.EscapeMarkup()}[/]");
            return false;
        }
        finally
        {
            if (File.Exists(probeOutput))
                File.Delete(probeOutput);
        }
    }

    private static bool IsEac3Audio(OutputFile file)
    {
        return file.Mediainfos.Any(info =>
            string.Equals(info.Type, "Audio", StringComparison.OrdinalIgnoreCase) &&
            ((info.BaseInfo?.Contains("eac3", StringComparison.OrdinalIgnoreCase) ?? false) ||
             (info.Text?.Contains("ec-3", StringComparison.OrdinalIgnoreCase) ?? false)));
    }

    public static bool MuxInputsByMkvmerge(string binary, OutputFile[] files, string outputPath)
    {
        StringBuilder command = new StringBuilder($"-q --output \"{outputPath}.mkv\" ");

        command.Append(" --no-chapters ");

        var dFlag = false;

        // LANG and NAME
        for (int i = 0; i < files.Length; i++)
        {
            // 转换语言代码
            LanguageCodeUtil.ConvertLangCodeAndDisplayName(files[i]);
            command.Append($" --language 0:\"{files[i].LangCode ?? "und"}\" ");
            // 字幕都不设置默认
            if (files[i].MediaType == Common.Enum.MediaType.SUBTITLES)
                command.Append($" --default-track 0:no ");
            // 音频除了第一个音轨 都不设置默认
            if (files[i].MediaType == Common.Enum.MediaType.AUDIO)
            {
                if (dFlag)
                    command.Append($" --default-track 0:no ");
                dFlag = true;
            }
            if (!string.IsNullOrEmpty(files[i].Description))
                command.Append($" --track-name 0:\"{files[i].Description}\" ");
            command.Append($" \"{files[i].FilePath}\" ");
        }

        var code = InvokeFFmpeg(binary, command.ToString(), Environment.CurrentDirectory);

        return code == 0;
    }
}
