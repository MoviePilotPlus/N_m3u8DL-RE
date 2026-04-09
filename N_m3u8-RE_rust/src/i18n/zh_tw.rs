use std::collections::HashMap;
use lazy_static::lazy_static;

lazy_static! {
    pub static ref TRANSLATIONS: HashMap<String, String> = {
        let mut map = HashMap::new();
        map.insert("app_name".to_string(), "N_m3u8-RE".to_string());
        map.insert("version".to_string(), "版本".to_string());
        map.insert("loading_stream".to_string(), "載入媒體流...".to_string());
        map.insert("found_streams".to_string(), "找到 {} 個流:".to_string());
        map.insert("no_streams_found".to_string(), "沒有找到可下載的流".to_string());
        map.insert("downloading".to_string(), "下載中...".to_string());
        map.insert("recording".to_string(), "錄製中...".to_string());
        map.insert("download_completed".to_string(), "下載完成！".to_string());
        map.insert("download_failed".to_string(), "下載失敗！".to_string());
        map.insert("recording_completed".to_string(), "錄製完成！".to_string());
        map.insert("recording_failed".to_string(), "錄製失敗！".to_string());
        map.insert("ffmpeg_not_found".to_string(), "未找到FFmpeg".to_string());
        map.insert("mkvmerge_not_found".to_string(), "未找到MKVMerge".to_string());
        map.insert("shaka_packager_not_found".to_string(), "未找到Shaka Packager".to_string());
        map.insert("mp4decrypt_not_found".to_string(), "未找到mp4decrypt".to_string());
        map.insert("console_redirected".to_string(), "控制台輸出已重新導向".to_string());
        map.insert("new_version_found".to_string(), "發現新版本".to_string());
        map.insert("live_found".to_string(), "發現直播流".to_string());
        map.insert("auto_binary_merge3".to_string(), "檢測到未知的加密方式，自動開啟二進制合併".to_string());
        map.insert("auto_binary_merge6".to_string(), "開啟了MuxAfterDone，自動開啟二進制合併".to_string());
        map.insert("write_json".to_string(), "正在寫入JSON文件".to_string());
        map.insert("selected_stream".to_string(), "已選擇的流:".to_string());
        map.insert("streams_info".to_string(), "流資訊：共 {} 個，{} 個視頻，{} 個音頻，{} 個字幕".to_string());
        map.insert("task_start_at".to_string(), "任務將在以下時間開始：".to_string());
        map.insert("save_name".to_string(), "保存名稱：".to_string());
        map
    };
}
