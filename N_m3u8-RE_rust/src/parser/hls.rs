use std::collections::HashMap;
use url::Url;
use crate::entity::stream::{StreamSpec, MediaType};
use crate::entity::playlist::{Playlist, MediaPart};
use crate::entity::segment::{MediaSegment, EncryptInfo, EncryptMethod};

#[derive(Debug, Clone)]
pub struct HLSExtractor {
    base_url: Option<String>,
    headers: HashMap<String, String>,
}

impl HLSExtractor {
    pub fn new(base_url: Option<String>, headers: HashMap<String, String>) -> Self {
        Self {
            base_url,
            headers,
        }
    }
    
    pub fn extract_streams(&self, content: &str) -> Result<Vec<StreamSpec>, Box<dyn std::error::Error>> {
        let mut streams = Vec::new();
        let lines: Vec<&str> = content.lines().collect();
        
        let mut current_stream = StreamSpec::default();
        let mut current_playlist = Playlist::default();
        let mut current_media_part = MediaPart::default();
        let mut current_segment = MediaSegment::default();
        let mut in_playlist = false;
        
        for line in lines {
            let line = line.trim();
            
            if line.starts_with("#EXTM3U") {
                // 开始解析m3u8文件
            } else if line.starts_with("#EXT-X-STREAM-INF:") {
                // 处理之前的播放列表
                if !current_media_part.media_segments.is_empty() {
                    current_playlist.media_parts.push(current_media_part.clone());
                    current_media_part = MediaPart::default();
                }
                
                // 解析流信息
                current_stream = self.parse_stream_inf(line);
                current_playlist = Playlist::default();
                in_playlist = false;
            } else if line.starts_with("#EXT-X-MEDIA:") {
                // 解析媒体信息
                // 暂时留空
            } else if line.starts_with("#EXT-X-PLAYLIST-TYPE:") {
                // 解析播放列表类型
                if line.contains("VOD") {
                    current_playlist.is_live = false;
                } else if line.contains("EVENT") || line.contains("LIVE") {
                    current_playlist.is_live = true;
                }
            } else if line.starts_with("#EXT-X-TARGETDURATION:") {
                // 解析目标时长
                if let Some(duration) = line.split(':').nth(1) {
                    if let Ok(d) = duration.parse::<f64>() {
                        current_playlist.target_duration = Some(d);
                    }
                }
            } else if line.starts_with("#EXT-X-MAP:") {
                // 解析初始化片段
                if let Some((_, map_info)) = line.split_once(':') {
                    for param in map_info.split(',') {
                        let param = param.trim();
                        if let Some((key, value)) = param.split_once('=') {
                            if key == "URI" {
                                let init_segment = value.trim_matches('"').to_string();
                                current_playlist.init_segment = Some(self.resolve_url(&init_segment));
                            }
                        }
                    }
                }
            } else if line.starts_with("#EXTINF:") {
                // 解析分片信息
                if let Some(info) = line.split(':').nth(1) {
                    if let Some((duration, title)) = info.split_once(',') {
                        if let Ok(d) = duration.parse::<f64>() {
                            current_segment.duration = d;
                        }
                        if !title.trim().is_empty() {
                            current_segment.title = Some(title.trim().to_string());
                        }
                    }
                }
                in_playlist = true;
            } else if line.starts_with("#EXT-X-KEY:") {
                // 解析加密信息
                current_segment.encrypt_info = Some(self.parse_key_info(line));
            } else if line.starts_with("#EXT-X-ENDLIST") {
                // 播放列表结束
                current_playlist.is_ended = true;
            } else if !line.starts_with('#') && !line.is_empty() {
                if in_playlist {
                    // 解析分片URL
                    current_segment.uri = self.resolve_url(line);
                    current_media_part.media_segments.push(current_segment.clone());
                    current_segment = MediaSegment::default();
                } else {
                    // 解析流URL
                    current_stream.id = self.resolve_url(line);
                    
                    // 处理当前播放列表
                    if !current_media_part.media_segments.is_empty() {
                        current_playlist.media_parts.push(current_media_part.clone());
                    }
                    
                    // 关联播放列表到流
                    current_stream.playlist = Some(Box::new(current_playlist.clone()));
                    streams.push(current_stream.clone());
                    
                    // 重置播放列表和媒体部分
                    current_playlist = Playlist::default();
                    current_media_part = MediaPart::default();
                }
            }
        }
        
        // 处理最后一个流
        if !current_stream.id.is_empty() && current_stream.playlist.is_none() {
            if !current_media_part.media_segments.is_empty() {
                current_playlist.media_parts.push(current_media_part.clone());
            }
            current_stream.playlist = Some(Box::new(current_playlist.clone()));
            streams.push(current_stream.clone());
        }
        
        Ok(streams)
    }
    
    fn parse_stream_inf(&self, line: &str) -> StreamSpec {
        let mut stream = StreamSpec::default();
        let inf_part = line.trim_start_matches("#EXT-X-STREAM-INF:");
        
        for param in inf_part.split(',') {
            let param = param.trim();
            if let Some((key, value)) = param.split_once('=') {
                match key {
                    "BANDWIDTH" => {
                        if let Ok(bandwidth) = value.parse::<u32>() {
                            stream.bandwidth = bandwidth;
                        }
                    }
                    "CODECS" => {
                        stream.codecs = value.trim_matches('"').to_string();
                    }
                    "RESOLUTION" => {
                        stream.resolution = Some(value.to_string());
                    }
                    "FRAME-RATE" => {
                        if let Ok(frame_rate) = value.parse::<f32>() {
                            stream.frame_rate = Some(frame_rate);
                        }
                    }
                    "AUDIO" => {
                        stream.media_type = Some(MediaType::AUDIO);
                    }
                    "SUBTITLES" => {
                        stream.media_type = Some(MediaType::SUBTITLES);
                    }
                    _ => {}
                }
            }
        }
        
        // 默认媒体类型为视频
        if stream.media_type.is_none() {
            stream.media_type = Some(MediaType::VIDEO);
        }
        
        stream
    }
    
    fn parse_key_info(&self, line: &str) -> EncryptInfo {
        let mut encrypt_info = EncryptInfo::default();
        let key_part = line.trim_start_matches("#EXT-X-KEY:");
        
        for param in key_part.split(',') {
            let param = param.trim();
            if let Some((key, value)) = param.split_once('=') {
                match key {
                    "METHOD" => {
                        encrypt_info.method = match value {
                            "AES-128" => EncryptMethod::AES_128,
                            "NONE" => EncryptMethod::NONE,
                            _ => EncryptMethod::UNKNOWN,
                        };
                    }
                    "URI" => {
                        let key_uri = value.trim_matches('"').to_string();
                        encrypt_info.key_uri = Some(self.resolve_url(&key_uri));
                    }
                    "IV" => {
                        let iv_hex = value.trim_matches('"').trim_start_matches("0x");
                        if let Ok(iv) = hex::decode(iv_hex) {
                            encrypt_info.iv = Some(iv);
                        }
                    }
                    _ => {}
                }
            }
        }
        
        encrypt_info
    }
    
    fn resolve_url(&self, url: &str) -> String {
        if url.starts_with("http://") || url.starts_with("https://") {
            return url.to_string();
        }
        
        if let Some(base_url) = &self.base_url {
            if let Ok(base) = url::Url::parse(base_url) {
                if let Ok(resolved) = base.join(url) {
                    return resolved.to_string();
                }
            }
        }
        
        url.to_string()
    }
}
