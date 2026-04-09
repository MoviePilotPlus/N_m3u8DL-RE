use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StreamSpec {
    pub id: String,
    pub media_type: Option<MediaType>,
    pub codecs: String,
    pub language: Option<String>,
    pub name: Option<String>,
    pub resolution: Option<String>,
    pub bandwidth: u32,
    pub channels: Option<String>,
    pub frame_rate: Option<f32>,
    pub video_range: Option<String>,
    pub group_id: Option<String>,
    pub playlist: Option<Box<crate::entity::playlist::Playlist>>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub enum MediaType {
    VIDEO,
    AUDIO,
    SUBTITLES,
}

impl std::fmt::Display for MediaType {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            MediaType::VIDEO => write!(f, "VIDEO"),
            MediaType::AUDIO => write!(f, "AUDIO"),
            MediaType::SUBTITLES => write!(f, "SUBTITLES"),
        }
    }
}

impl Default for StreamSpec {
    fn default() -> Self {
        Self {
            id: String::new(),
            media_type: None,
            codecs: String::new(),
            language: None,
            name: None,
            resolution: None,
            bandwidth: 0,
            channels: None,
            frame_rate: None,
            video_range: None,
            group_id: None,
            playlist: None,
        }
    }
}
