use std::collections::HashMap;
use lazy_static::lazy_static;

lazy_static! {
    pub static ref TRANSLATIONS: HashMap<String, String> = {
        let mut map = HashMap::new();
        map.insert("app_name".to_string(), "N_m3u8-RE".to_string());
        map.insert("version".to_string(), "版本".to_string());
        map.insert("loading_stream".to_string(), "加载媒体流...".to_string());
        map.insert("found_streams".to_string(), "找到 {} 个流:".to_string());
        map.insert("no_streams_found".to_string(), "没有找到可下载的流".to_string());
        map.insert("downloading".to_string(), "下载中...".to_string());
        map.insert("recording".to_string(), "录制中...".to_string());
        map.insert("download_completed".to_string(), "下载完成！".to_string());
        map.insert("download_failed".to_string(), "下载失败！".to_string());
        map.insert("recording_completed".to_string(), "录制完成！".to_string());
        map.insert("recording_failed".to_string(), "录制失败！".to_string());
        map.insert("ffmpeg_not_found".to_string(), "未找到FFmpeg".to_string());
        map.insert("mkvmerge_not_found".to_string(), "未找到MKVMerge".to_string());
        map.insert("shaka_packager_not_found".to_string(), "未找到Shaka Packager".to_string());
        map.insert("mp4decrypt_not_found".to_string(), "未找到mp4decrypt".to_string());
        map.insert("console_redirected".to_string(), "控制台输出已重定向".to_string());
        map.insert("new_version_found".to_string(), "发现新版本".to_string());
        map.insert("live_found".to_string(), "发现直播流".to_string());
        map.insert("auto_binary_merge3".to_string(), "检测到未知的加密方式，自动开启二进制合并".to_string());
        map.insert("auto_binary_merge6".to_string(), "开启了MuxAfterDone，自动开启二进制合并".to_string());
        map.insert("write_json".to_string(), "正在写入JSON文件".to_string());
        map.insert("selected_stream".to_string(), "已选择的流:".to_string());
        map.insert("streams_info".to_string(), "流信息：共 {} 个，{} 个视频，{} 个音频，{} 个字幕".to_string());
        map.insert("task_start_at".to_string(), "任务将在以下时间开始：".to_string());
        map.insert("save_name".to_string(), "保存名称：".to_string());
        map
    };
}
