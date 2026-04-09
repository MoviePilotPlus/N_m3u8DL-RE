use std::collections::HashMap;
use lazy_static::lazy_static;

lazy_static! {
    pub static ref TRANSLATIONS: HashMap<String, String> = {
        let mut map = HashMap::new();
        map.insert("app_name".to_string(), "N_m3u8-RE".to_string());
        map.insert("version".to_string(), "Version".to_string());
        map.insert("loading_stream".to_string(), "Loading stream...".to_string());
        map.insert("found_streams".to_string(), "Found {} streams:".to_string());
        map.insert("no_streams_found".to_string(), "No streams found".to_string());
        map.insert("downloading".to_string(), "Downloading...".to_string());
        map.insert("recording".to_string(), "Recording...".to_string());
        map.insert("download_completed".to_string(), "Download completed!".to_string());
        map.insert("download_failed".to_string(), "Download failed!".to_string());
        map.insert("recording_completed".to_string(), "Recording completed!".to_string());
        map.insert("recording_failed".to_string(), "Recording failed!".to_string());
        map.insert("ffmpeg_not_found".to_string(), "FFmpeg not found".to_string());
        map.insert("mkvmerge_not_found".to_string(), "MKVMerge not found".to_string());
        map.insert("shaka_packager_not_found".to_string(), "Shaka Packager not found".to_string());
        map.insert("mp4decrypt_not_found".to_string(), "mp4decrypt not found".to_string());
        map.insert("console_redirected".to_string(), "Console output is redirected".to_string());
        map.insert("new_version_found".to_string(), "New version found".to_string());
        map.insert("live_found".to_string(), "Live stream found".to_string());
        map.insert("auto_binary_merge3".to_string(), "Unknown encryption method detected, automatically enabling binary merge".to_string());
        map.insert("auto_binary_merge6".to_string(), "MuxAfterDone enabled, automatically enabling binary merge".to_string());
        map.insert("write_json".to_string(), "Writing JSON files".to_string());
        map.insert("selected_stream".to_string(), "Selected streams:".to_string());
        map.insert("streams_info".to_string(), "Stream information: {} total, {} video, {} audio, {} subtitle".to_string());
        map.insert("task_start_at".to_string(), "Task will start at: ".to_string());
        map.insert("save_name".to_string(), "Save name: ".to_string());
        map
    };
}
