use std::collections::HashMap;
use quick_xml::de::from_str;
use serde::Deserialize;
use url::Url;
use crate::entity::stream::{StreamSpec, MediaType};
use crate::entity::playlist::{Playlist, MediaPart};
use crate::entity::segment::MediaSegment;

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmoothStreamingMedia {
    #[serde(rename = "@MajorVersion")]
    pub major_version: u32,
    #[serde(rename = "@MinorVersion")]
    pub minor_version: u32,
    #[serde(rename = "@TimeScale")]
    pub time_scale: u32,
    #[serde(rename = "@Duration")]
    pub duration: Option<f64>,
    #[serde(rename = "@IsLive")]
    pub is_live: Option<bool>,
    pub stream_index: Vec<StreamIndex>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StreamIndex {
    #[serde(rename = "@Type")]
    pub stream_type: String,
    #[serde(rename = "@QualityLevels")]
    pub quality_levels: String,
    #[serde(rename = "@Chunks")]
    pub chunks: String,
    #[serde(rename = "@Url")]
    pub url: Option<String>,
    pub quality_level: Vec<QualityLevel>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct QualityLevel {
    #[serde(rename = "@Index")]
    pub index: u32,
    #[serde(rename = "@Bitrate")]
    pub bitrate: u32,
    #[serde(rename = "@CodecPrivateData")]
    pub codec_private_data: Option<String>,
    #[serde(rename = "@MaxWidth")]
    pub max_width: Option<u32>,
    #[serde(rename = "@MaxHeight")]
    pub max_height: Option<u32>,
    #[serde(rename = "@FrameRate")]
    pub frame_rate: Option<f32>,
    #[serde(rename = "@FourCC")]
    pub four_cc: Option<String>,
}

#[derive(Debug, Clone)]
pub struct MSSExtractor {
    base_url: Option<String>,
    headers: HashMap<String, String>,
}

impl MSSExtractor {
    pub fn new(base_url: Option<String>, headers: HashMap<String, String>) -> Self {
        Self {
            base_url,
            headers,
        }
    }
    
    pub fn extract_streams(&self, content: &str) -> Result<Vec<StreamSpec>, Box<dyn std::error::Error>> {
        let mut streams = Vec::new();
        
        // 解析SmoothStreamingMedia XML
        let media: SmoothStreamingMedia = from_str(content)?;
        
        for stream_index in media.stream_index {
            let media_type = self.determine_media_type(&stream_index.stream_type);
            
            for quality_level in &stream_index.quality_level {
                let mut stream = StreamSpec::default();
                stream.id = format!("{}_{}", stream_index.stream_type, quality_level.index);
                stream.media_type = media_type.clone();
                stream.bandwidth = quality_level.bitrate;
                
                // 解析分辨率
                if let (Some(width), Some(height)) = (quality_level.max_width, quality_level.max_height) {
                    stream.resolution = Some(format!("{}x{}", width, height));
                }
                
                // 解析帧率
                stream.frame_rate = quality_level.frame_rate;
                
                // 解析编解码器信息
                if let Some(codec_data) = &quality_level.codec_private_data {
                    stream.codecs = self.parse_codec_data(codec_data);
                }
                
                // 构建播放列表
                if let Some(url_template) = &stream_index.url {
                    let playlist = self.build_playlist(&stream_index, quality_level, url_template);
                    stream.playlist = Some(Box::new(playlist));
                }
                
                streams.push(stream);
            }
        }
        
        Ok(streams)
    }
    
    fn build_playlist(&self, stream_index: &StreamIndex, quality_level: &QualityLevel, url_template: &str) -> Playlist {
        let mut playlist = Playlist::default();
        let mut media_part = MediaPart::default();
        
        // 解析分片数量
        let chunks: u32 = stream_index.chunks.parse().unwrap_or(10);
        
        // 生成分片URL
        for i in 0..chunks {
            let segment_url = self.resolve_url(&url_template
                .replace("{bitrate}", &quality_level.bitrate.to_string())
                .replace("{start time}", &(i * 2000).to_string())
            );
            let mut segment = MediaSegment::default();
            segment.uri = segment_url;
            segment.duration = 2.0; // 假设每个分片2秒
            media_part.media_segments.push(segment);
        }
        
        if !media_part.media_segments.is_empty() {
            playlist.media_parts.push(media_part);
        }
        
        playlist
    }
    
    fn resolve_url(&self, url: &str) -> String {
        if url.starts_with("http://") || url.starts_with("https://") {
            return url.to_string();
        }
        
        if let Some(base_url) = &self.base_url {
            if let Ok(base) = Url::parse(base_url) {
                if let Ok(resolved) = base.join(url) {
                    return resolved.to_string();
                }
            }
        }
        
        url.to_string()
    }
    
    fn determine_media_type(&self, stream_type: &str) -> Option<MediaType> {
        match stream_type {
            "video" => Some(MediaType::VIDEO),
            "audio" => Some(MediaType::AUDIO),
            "text" => Some(MediaType::SUBTITLES),
            _ => None,
        }
    }
    
    fn parse_codec_data(&self, codec_data: &str) -> String {
        // 这里应该实现编解码器数据的解析
        // 暂时返回原始数据
        codec_data.to_string()
    }
}
