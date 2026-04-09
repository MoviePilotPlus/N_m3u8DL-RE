use std::collections::HashMap;
use quick_xml::de::from_str;
use serde::Deserialize;
use url::Url;
use crate::entity::stream::{StreamSpec, MediaType};
use crate::entity::playlist::{Playlist, MediaPart};
use crate::entity::segment::MediaSegment;

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MPD {
    pub period: Period,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Period {
    pub adaptation_set: Vec<AdaptationSet>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AdaptationSet {
    #[serde(rename = "@mimeType")]
    pub mime_type: Option<String>,
    #[serde(rename = "@codecs")]
    pub codecs: Option<String>,
    #[serde(rename = "@lang")]
    pub lang: Option<String>,
    #[serde(rename = "@width")]
    pub width: Option<u32>,
    #[serde(rename = "@height")]
    pub height: Option<u32>,
    #[serde(rename = "@frameRate")]
    pub frame_rate: Option<f32>,
    pub representation: Vec<Representation>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Representation {
    #[serde(rename = "@id")]
    pub id: String,
    #[serde(rename = "@bandwidth")]
    pub bandwidth: u32,
    #[serde(rename = "@codecs")]
    pub codecs: Option<String>,
    #[serde(rename = "@width")]
    pub width: Option<u32>,
    #[serde(rename = "@height")]
    pub height: Option<u32>,
    #[serde(rename = "@frameRate")]
    pub frame_rate: Option<f32>,
    pub base_url: Option<BaseURL>,
    pub segment_template: Option<SegmentTemplate>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct BaseURL {
    #[serde(rename = "#text")]
    pub text: String,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SegmentTemplate {
    #[serde(rename = "@initialization")]
    pub initialization: Option<String>,
    #[serde(rename = "@media")]
    pub media: Option<String>,
    #[serde(rename = "@startNumber")]
    pub start_number: Option<u64>,
    #[serde(rename = "@duration")]
    pub duration: Option<f64>,
}

#[derive(Debug, Clone)]
pub struct DASHExtractor {
    base_url: Option<String>,
    headers: HashMap<String, String>,
}

impl DASHExtractor {
    pub fn new(base_url: Option<String>, headers: HashMap<String, String>) -> Self {
        Self {
            base_url,
            headers,
        }
    }
    
    pub fn extract_streams(&self, content: &str) -> Result<Vec<StreamSpec>, Box<dyn std::error::Error>> {
        let mut streams = Vec::new();
        
        // 解析MPD XML
        let mpd: MPD = from_str(content)?;
        
        for adaptation_set in mpd.period.adaptation_set {
            let media_type = self.determine_media_type(&adaptation_set.mime_type);
            
            for representation in adaptation_set.representation {
                let mut stream = StreamSpec::default();
                stream.id = representation.id.clone();
                stream.media_type = media_type.clone();
                stream.codecs = representation.codecs.clone().unwrap_or(adaptation_set.codecs.clone().unwrap_or(String::new()));
                stream.language = adaptation_set.lang.clone();
                stream.bandwidth = representation.bandwidth;
                
                // 解析分辨率
                if let (Some(width), Some(height)) = (representation.width.or(adaptation_set.width), representation.height.or(adaptation_set.height)) {
                    stream.resolution = Some(format!("{}x{}", width, height));
                }
                
                // 解析帧率
                stream.frame_rate = representation.frame_rate.or(adaptation_set.frame_rate);
                
                // 构建播放列表
                if let Some(segment_template) = &representation.segment_template {
                    let playlist = self.build_playlist(&representation, segment_template);
                    stream.playlist = Some(Box::new(playlist));
                }
                
                streams.push(stream);
            }
        }
        
        Ok(streams)
    }
    
    fn build_playlist(&self, representation: &Representation, segment_template: &SegmentTemplate) -> Playlist {
        let mut playlist = Playlist::default();
        let mut media_part = MediaPart::default();
        
        // 解析初始化片段
        if let Some(initialization) = &segment_template.initialization {
            let init_url = self.resolve_url(initialization);
            playlist.init_segment = Some(init_url);
        }
        
        // 解析媒体片段
        if let Some(media) = &segment_template.media {
            // 假设从startNumber开始，生成10个片段（实际应该根据MPD中的信息计算）
            let start_number = segment_template.start_number.unwrap_or(1);
            for i in start_number..start_number + 10 {
                let segment_url = self.resolve_url(&media.replace("$Number$", &i.to_string()));
                let mut segment = MediaSegment::default();
                segment.uri = segment_url;
                segment.duration = segment_template.duration.unwrap_or(2.0);
                media_part.media_segments.push(segment);
            }
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
    
    fn determine_media_type(&self, mime_type: &Option<String>) -> Option<MediaType> {
        match mime_type {
            Some(mime) if mime.starts_with("video/") => Some(MediaType::VIDEO),
            Some(mime) if mime.starts_with("audio/") => Some(MediaType::AUDIO),
            Some(mime) if mime.starts_with("text/") || mime.starts_with("application/ttml") => Some(MediaType::SUBTITLES),
            _ => None,
        }
    }
}
