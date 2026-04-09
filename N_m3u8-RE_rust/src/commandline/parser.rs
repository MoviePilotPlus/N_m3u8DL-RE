use clap::{Arg, ArgAction, Command, ValueHint};
use crate::commandline::options::MyOption;
use std::str::FromStr;
use chrono::DateTime;
use chrono::Utc;
use url::Url;

pub fn parse_args() -> MyOption {
    let matches = Command::new("N_m3u8-RE")
        .version("0.1.0")
        .about("Cross-platform DASH/HLS/MSS downloader")
        .arg(
            Arg::new("input")
                .help("链接或文件")
                .required(true)
                .value_hint(ValueHint::Url)
        )
        .arg(
            Arg::new("tmp-dir")
                .long("tmp-dir")
                .help("设置临时文件存储目录")
                .value_name("tmp-dir")
        )
        .arg(
            Arg::new("save-dir")
                .long("save-dir")
                .help("设置输出目录")
                .value_name("save-dir")
        )
        .arg(
            Arg::new("save-name")
                .long("save-name")
                .help("设置保存文件名")
                .value_name("save-name")
        )
        .arg(
            Arg::new("save-pattern")
                .long("save-pattern")
                .help("设置保存文件命名模板")
                .value_name("save-pattern")
        )
        .arg(
            Arg::new("log-file-path")
                .long("log-file-path")
                .help("设置日志文件路径")
                .value_name("log-file-path")
        )
        .arg(
            Arg::new("base-url")
                .long("base-url")
                .help("设置BaseURL")
                .value_name("base-url")
        )
        .arg(
            Arg::new("thread-count")
                .long("thread-count")
                .help("设置下载线程数")
                .value_name("number")
                .value_parser(clap::value_parser!(usize))
        )
        .arg(
            Arg::new("download-retry-count")
                .long("download-retry-count")
                .help("每个分片下载异常时的重试次数")
                .value_name("number")
                .value_parser(clap::value_parser!(usize))
        )
        .arg(
            Arg::new("http-request-timeout")
                .long("http-request-timeout")
                .help("HTTP请求的超时时间(秒)")
                .value_name("seconds")
                .value_parser(clap::value_parser!(f64))
        )
        .arg(
            Arg::new("force-ansi-console")
                .long("force-ansi-console")
                .help("强制认定终端为支持ANSI且可交互的终端")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("no-ansi-color")
                .long("no-ansi-color")
                .help("去除ANSI颜色")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("auto-select")
                .long("auto-select")
                .help("自动选择所有类型的最佳轨道")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("skip-merge")
                .long("skip-merge")
                .help("跳过合并分片")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("skip-download")
                .long("skip-download")
                .help("跳过下载")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("check-segments-count")
                .long("check-segments-count")
                .help("检测实际下载的分片数量和预期数量是否匹配")
                .action(ArgAction::SetFalse)
        )
        .arg(
            Arg::new("binary-merge")
                .long("binary-merge")
                .help("二进制合并")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("use-ffmpeg-concat-demuxer")
                .long("use-ffmpeg-concat-demuxer")
                .help("使用 ffmpeg 合并时，使用 concat 分离器而非 concat 协议")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("del-after-done")
                .long("del-after-done")
                .help("完成后删除临时文件")
                .action(ArgAction::SetFalse)
        )
        .arg(
            Arg::new("no-date-info")
                .long("no-date-info")
                .help("混流时不写入日期信息")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("no-log")
                .long("no-log")
                .help("关闭日志文件输出")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("write-meta-json")
                .long("write-meta-json")
                .help("解析后的信息是否输出json文件")
                .action(ArgAction::SetFalse)
        )
        .arg(
            Arg::new("append-url-params")
                .long("append-url-params")
                .help("将输入Url的Params添加至分片")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("concurrent-download")
                .short("mt")
                .long("concurrent-download")
                .help("并发下载已选择的音频、视频和字幕")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("header")
                .short('H')
                .long("header")
                .help("为HTTP请求设置特定的请求头")
                .value_name("header")
                .action(ArgAction::Append)
        )
        .arg(
            Arg::new("sub-only")
                .long("sub-only")
                .help("只选取字幕轨道")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("sub-format")
                .long("sub-format")
                .help("字幕输出类型")
                .value_name("SRT|VTT")
                .default_value("SRT")
                .value_parser(["SRT", "VTT"])
        )
        .arg(
            Arg::new("auto-subtitle-fix")
                .long("auto-subtitle-fix")
                .help("自动修正字幕")
                .action(ArgAction::SetFalse)
        )
        .arg(
            Arg::new("ffmpeg-binary-path")
                .long("ffmpeg-binary-path")
                .help("ffmpeg可执行程序全路径")
                .value_name("PATH")
        )
        .arg(
            Arg::new("log-level")
                .long("log-level")
                .help("设置日志级别")
                .value_name("DEBUG|ERROR|INFO|OFF|WARN")
                .default_value("INFO")
                .value_parser(["DEBUG", "ERROR", "INFO", "OFF", "WARN"])
        )
        .arg(
            Arg::new("ui-language")
                .long("ui-language")
                .help("设置UI语言")
                .value_name("en-US|zh-CN|zh-TW")
                .value_parser(["en-US", "zh-CN", "zh-TW"])
        )
        .arg(
            Arg::new("urlprocessor-args")
                .long("urlprocessor-args")
                .help("此字符串将直接传递给URL Processor")
                .value_name("urlprocessor-args")
        )
        .arg(
            Arg::new("key")
                .long("key")
                .help("设置解密密钥")
                .value_name("key")
                .action(ArgAction::Append)
        )
        .arg(
            Arg::new("key-text-file")
                .long("key-text-file")
                .help("设置密钥文件")
                .value_name("key-text-file")
        )
        .arg(
            Arg::new("decryption-engine")
                .long("decryption-engine")
                .help("设置解密时使用的第三方程序")
                .value_name("FFMPEG|MP4DECRYPT|SHAKA_PACKAGER")
                .default_value("MP4DECRYPT")
                .value_parser(["FFMPEG", "MP4DECRYPT", "SHAKA_PACKAGER"])
        )
        .arg(
            Arg::new("decryption-binary-path")
                .long("decryption-binary-path")
                .help("MP4解密所用工具的全路径")
                .value_name("PATH")
        )
        .arg(
            Arg::new("mp4-real-time-decryption")
                .long("mp4-real-time-decryption")
                .help("实时解密MP4分片")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("max-speed")
                .short('R')
                .long("max-speed")
                .help("设置限速，单位支持 Mbps 或 Kbps")
                .value_name("SPEED")
        )
        .arg(
            Arg::new("mux-after-done")
                .short('M')
                .long("mux-after-done")
                .help("所有工作完成时尝试混流分离的音视频")
                .value_name("OPTIONS")
        )
        .arg(
            Arg::new("custom-hls-method")
                .long("custom-hls-method")
                .help("指定HLS加密方式")
                .value_name("METHOD")
                .value_parser(["AES_128", "AES_128_ECB", "CENC", "CHACHA20", "NONE", "SAMPLE_AES", "SAMPLE_AES_CTR", "UNKNOWN"])
        )
        .arg(
            Arg::new("custom-hls-key")
                .long("custom-hls-key")
                .help("指定HLS解密KEY")
                .value_name("FILE|HEX|BASE64")
        )
        .arg(
            Arg::new("custom-hls-iv")
                .long("custom-hls-iv")
                .help("指定HLS解密IV")
                .value_name("FILE|HEX|BASE64")
        )
        .arg(
            Arg::new("use-system-proxy")
                .long("use-system-proxy")
                .help("使用系统默认代理")
                .action(ArgAction::SetFalse)
        )
        .arg(
            Arg::new("custom-proxy")
                .long("custom-proxy")
                .help("设置请求代理")
                .value_name("URL")
        )
        .arg(
            Arg::new("custom-range")
                .long("custom-range")
                .help("仅下载部分分片")
                .value_name("RANGE")
        )
        .arg(
            Arg::new("task-start-at")
                .long("task-start-at")
                .help("在此时间之前不会开始执行任务")
                .value_name("yyyyMMddHHmmss")
        )
        .arg(
            Arg::new("live-perform-as-vod")
                .long("live-perform-as-vod")
                .help("以点播方式下载直播流")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("live-real-time-merge")
                .long("live-real-time-merge")
                .help("录制直播时实时合并")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("live-keep-segments")
                .long("live-keep-segments")
                .help("录制直播并开启实时合并时依然保留分片")
                .action(ArgAction::SetFalse)
        )
        .arg(
            Arg::new("live-pipe-mux")
                .long("live-pipe-mux")
                .help("录制直播并开启实时合并时通过管道+ffmpeg实时混流到TS文件")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("live-fix-vtt-by-audio")
                .long("live-fix-vtt-by-audio")
                .help("通过读取音频文件的起始时间修正VTT字幕")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("live-record-limit")
                .long("live-record-limit")
                .help("录制直播时的录制时长限制")
                .value_name("HH:mm:ss")
        )
        .arg(
            Arg::new("live-wait-time")
                .long("live-wait-time")
                .help("手动设置直播列表刷新间隔")
                .value_name("SEC")
                .value_parser(clap::value_parser!(u64))
        )
        .arg(
            Arg::new("live-take-count")
                .long("live-take-count")
                .help("手动设置录制直播时首次获取分片的数量")
                .value_name("NUM")
                .value_parser(clap::value_parser!(usize))
        )
        .arg(
            Arg::new("mux-import")
                .long("mux-import")
                .help("混流时引入外部媒体文件")
                .value_name("OPTIONS")
                .action(ArgAction::Append)
        )
        .arg(
            Arg::new("select-video")
                .short("sv")
                .long("select-video")
                .help("通过正则表达式选择符合要求的视频流")
                .value_name("OPTIONS")
        )
        .arg(
            Arg::new("select-audio")
                .short("sa")
                .long("select-audio")
                .help("通过正则表达式选择符合要求的音频流")
                .value_name("OPTIONS")
        )
        .arg(
            Arg::new("select-subtitle")
                .short("ss")
                .long("select-subtitle")
                .help("通过正则表达式选择符合要求的字幕流")
                .value_name("OPTIONS")
        )
        .arg(
            Arg::new("drop-video")
                .short("dv")
                .long("drop-video")
                .help("通过正则表达式去除符合要求的视频流")
                .value_name("OPTIONS")
        )
        .arg(
            Arg::new("drop-audio")
                .short("da")
                .long("drop-audio")
                .help("通过正则表达式去除符合要求的音频流")
                .value_name("OPTIONS")
        )
        .arg(
            Arg::new("drop-subtitle")
                .short("ds")
                .long("drop-subtitle")
                .help("通过正则表达式去除符合要求的字幕流")
                .value_name("OPTIONS")
        )
        .arg(
            Arg::new("ad-keyword")
                .long("ad-keyword")
                .help("设置广告分片的URL关键字(正则表达式)")
                .value_name("REG")
                .action(ArgAction::Append)
        )
        .arg(
            Arg::new("disable-update-check")
                .long("disable-update-check")
                .help("禁用版本更新检测")
                .action(ArgAction::SetTrue)
        )
        .arg(
            Arg::new("allow-hls-multi-ext-map")
                .long("allow-hls-multi-ext-map")
                .help("允许HLS中的多个#EXT-X-MAP(实验性)")
                .action(ArgAction::SetTrue)
        )
        .get_matches();

    let mut option = MyOption::default();
    
    // 基本参数
    option.input = matches.get_one::<String>("input").unwrap().to_string();
    
    // 目录和文件设置
    if let Some(tmp_dir) = matches.get_one::<String>("tmp-dir") {
        option.tmp_dir = Some(tmp_dir.to_string());
    }
    if let Some(save_dir) = matches.get_one::<String>("save-dir") {
        option.save_dir = Some(save_dir.to_string());
    }
    if let Some(save_name) = matches.get_one::<String>("save-name") {
        option.save_name = Some(save_name.to_string());
    }
    if let Some(save_pattern) = matches.get_one::<String>("save-pattern") {
        option.save_pattern = Some(save_pattern.to_string());
    }
    if let Some(log_file_path) = matches.get_one::<String>("log-file-path") {
        option.log_file_path = Some(log_file_path.to_string());
    }
    if let Some(base_url) = matches.get_one::<String>("base-url") {
        option.base_url = Some(base_url.to_string());
    }
    
    // 下载设置
    if let Some(thread_count) = matches.get_one::<usize>("thread-count") {
        option.thread_count = *thread_count;
    }
    if let Some(download_retry_count) = matches.get_one::<usize>("download-retry-count") {
        option.download_retry_count = *download_retry_count;
    }
    if let Some(http_request_timeout) = matches.get_one::<f64>("http-request-timeout") {
        option.http_request_timeout = *http_request_timeout;
    }
    
    // 行为设置
    option.force_ansi_console = matches.get_flag("force-ansi-console");
    option.no_ansi_color = matches.get_flag("no-ansi-color");
    option.auto_select = matches.get_flag("auto-select");
    option.skip_merge = matches.get_flag("skip-merge");
    option.skip_download = matches.get_flag("skip-download");
    option.check_segments_count = !matches.get_flag("check-segments-count");
    option.binary_merge = matches.get_flag("binary-merge");
    option.use_ffmpeg_concat_demuxer = matches.get_flag("use-ffmpeg-concat-demuxer");
    option.del_after_done = !matches.get_flag("del-after-done");
    option.no_date_info = matches.get_flag("no-date-info");
    option.no_log = matches.get_flag("no-log");
    option.write_meta_json = !matches.get_flag("write-meta-json");
    option.append_url_params = matches.get_flag("append-url-params");
    option.concurrent_download = matches.get_flag("concurrent-download");
    option.sub_only = matches.get_flag("sub-only");
    option.auto_subtitle_fix = !matches.get_flag("auto-subtitle-fix");
    option.mp4_real_time_decryption = matches.get_flag("mp4-real-time-decryption");
    option.skip_subtitle_decrypt = matches.get_flag("skip-subtitle-decrypt");
    option.skip_audio_decrypt = matches.get_flag("skip-audio-decrypt");
    option.force_mux_dolby = matches.get_flag("force-mux-dolby");
    option.use_shaka_packager = matches.get_flag("use-shaka-packager");
    option.mux_after_done = matches.get_flag("mux-after-done");
    option.live_real_time_merge = matches.get_flag("live-real-time-merge");
    option.live_keep_segments = !matches.get_flag("live-keep-segments");
    option.live_perform_as_vod = matches.get_flag("live-perform-as-vod");
    option.use_system_proxy = !matches.get_flag("use-system-proxy");
    option.live_pipe_mux = matches.get_flag("live-pipe-mux");
    option.live_fix_vtt_by_audio = matches.get_flag("live-fix-vtt-by-audio");
    option.disable_update_check = matches.get_flag("disable-update-check");
    option.allow_hls_multi_ext_map = matches.get_flag("allow-hls-multi-ext-map");
    
    // 其他设置
    if let Some(subtitle_format) = matches.get_one::<String>("sub-format") {
        option.subtitle_format = subtitle_format.to_string();
    }
    if let Some(ffmpeg_binary_path) = matches.get_one::<String>("ffmpeg-binary-path") {
        option.ffmpeg_binary_path = Some(ffmpeg_binary_path.to_string());
    }
    if let Some(log_level) = matches.get_one::<String>("log-level") {
        option.log_level = log_level.to_string();
    }
    if let Some(ui_language) = matches.get_one::<String>("ui-language") {
        option.ui_language = Some(ui_language.to_string());
    }
    if let Some(url_processor_args) = matches.get_one::<String>("urlprocessor-args") {
        option.url_processor_args = Some(url_processor_args.to_string());
    }
    if let Some(decryption_engine) = matches.get_one::<String>("decryption-engine") {
        option.decryption_engine = decryption_engine.to_string();
    }
    if let Some(decryption_binary_path) = matches.get_one::<String>("decryption-binary-path") {
        option.decryption_binary_path = Some(decryption_binary_path.to_string());
    }
    if let Some(key_text_file) = matches.get_one::<String>("key-text-file") {
        option.key_text_file = Some(key_text_file.to_string());
    }
    if let Some(mux_after_done) = matches.get_one::<String>("mux-after-done") {
        option.mux_options = Some(mux_after_done.to_string());
    }
    if let Some(custom_hls_method) = matches.get_one::<String>("custom-hls-method") {
        option.custom_hls_method = Some(custom_hls_method.to_string());
    }
    if let Some(custom_hls_key) = matches.get_one::<String>("custom-hls-key") {
        option.custom_hls_key = Some(custom_hls_key.to_string());
    }
    if let Some(custom_hls_iv) = matches.get_one::<String>("custom-hls-iv") {
        option.custom_hls_iv = Some(custom_hls_iv.to_string());
    }
    if let Some(custom_range) = matches.get_one::<String>("custom-range") {
        option.custom_range = Some(custom_range.to_string());
    }
    if let Some(live_wait_time) = matches.get_one::<u64>("live-wait-time") {
        option.live_wait_time = Some(*live_wait_time);
    }
    if let Some(live_take_count) = matches.get_one::<usize>("live-take-count") {
        option.live_take_count = *live_take_count;
    }
    
    // 列表参数
    if let Some(headers) = matches.get_many::<String>("header") {
        for header in headers {
            if let Some((key, value)) = header.split_once(':') {
                option.headers.insert(key.trim().to_string(), value.trim().to_string());
            }
        }
    }
    if let Some(keys) = matches.get_many::<String>("key") {
        option.keys = Some(keys.map(|k| k.to_string()).collect());
    }
    if let Some(ad_keywords) = matches.get_many::<String>("ad-keyword") {
        option.ad_keywords = Some(ad_keywords.map(|k| k.to_string()).collect());
    }
    if let Some(mux_imports) = matches.get_many::<String>("mux-import") {
        option.mux_imports = Some(mux_imports.map(|m| m.to_string()).collect());
    }
    
    // 视频、音频、字幕过滤器
    if let Some(video_filter) = matches.get_one::<String>("select-video") {
        option.video_filter = Some(video_filter.to_string());
    }
    if let Some(drop_video_filter) = matches.get_one::<String>("drop-video") {
        option.drop_video_filter = Some(drop_video_filter.to_string());
    }
    if let Some(audio_filter) = matches.get_one::<String>("select-audio") {
        option.audio_filter = Some(audio_filter.to_string());
    }
    if let Some(drop_audio_filter) = matches.get_one::<String>("drop-audio") {
        option.drop_audio_filter = Some(drop_audio_filter.to_string());
    }
    if let Some(subtitle_filter) = matches.get_one::<String>("select-subtitle") {
        option.subtitle_filter = Some(subtitle_filter.to_string());
    }
    if let Some(drop_subtitle_filter) = matches.get_one::<String>("drop-subtitle") {
        option.drop_subtitle_filter = Some(drop_subtitle_filter.to_string());
    }
    
    // 代理设置
    if let Some(custom_proxy) = matches.get_one::<String>("custom-proxy") {
        if let Ok(url) = Url::parse(custom_proxy) {
            option.custom_proxy = Some(url);
        }
    }
    
    // 时间设置
    if let Some(task_start_at) = matches.get_one::<String>("task-start-at") {
        if let Ok(dt) = DateTime::parse_from_str(task_start_at, "%Y%m%d%H%M%S") {
            option.task_start_at = Some(dt.with_timezone(&Utc));
        }
    }
    
    // 直播录制限制
    if let Some(live_record_limit) = matches.get_one::<String>("live-record-limit") {
        if let Ok(duration) = parse_duration(live_record_limit) {
            option.live_record_limit = Some(duration);
        }
    }
    
    // 限速设置
    if let Some(max_speed) = matches.get_one::<String>("max-speed") {
        if let Ok(speed) = parse_speed(max_speed) {
            option.max_speed = Some(speed);
        }
    }
    
    option
}

fn parse_duration(input: &str) -> Result<std::time::Duration, std::io::Error> {
    let parts: Vec<&str> = input.split(':').collect();
    if parts.len() != 3 {
        return Err(std::io::Error::new(std::io::ErrorKind::InvalidInput, "Invalid duration format"));
    }
    
    let hours: u64 = parts[0].parse().map_err(|_| std::io::Error::new(std::io::ErrorKind::InvalidInput, "Invalid hours"))?;
    let minutes: u64 = parts[1].parse().map_err(|_| std::io::Error::new(std::io::ErrorKind::InvalidInput, "Invalid minutes"))?;
    let seconds: u64 = parts[2].parse().map_err(|_| std::io::Error::new(std::io::ErrorKind::InvalidInput, "Invalid seconds"))?;
    
    Ok(std::time::Duration::from_secs(hours * 3600 + minutes * 60 + seconds))
}

fn parse_speed(input: &str) -> Result<u64, std::io::Error> {
    let input = input.trim().to_uppercase();
    
    if input.ends_with('M') {
        let value = input.trim_end_matches('M');
        let speed = value.parse::<f64>().map_err(|_| std::io::Error::new(std::io::ErrorKind::InvalidInput, "Invalid speed value"))?;
        Ok((speed * 1000000.0) as u64)
    } else if input.ends_with('K') {
        let value = input.trim_end_matches('K');
        let speed = value.parse::<f64>().map_err(|_| std::io::Error::new(std::io::ErrorKind::InvalidInput, "Invalid speed value"))?;
        Ok((speed * 1000.0) as u64)
    } else {
        Err(std::io::Error::new(std::io::ErrorKind::InvalidInput, "Invalid speed format"))
    }
}
