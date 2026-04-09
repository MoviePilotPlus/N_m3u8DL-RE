use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Playlist {
    pub is_live: bool,
    pub media_parts: Vec<MediaPart>,
    pub duration: Option<f64>,
    pub target_duration: Option<f64>,
    pub init_segment: Option<String>,
    pub is_ended: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MediaPart {
    pub uri: String,
    pub media_segments: Vec<crate::entity::segment::MediaSegment>,
    pub duration: Option<f64>,
}

impl Default for Playlist {
    fn default() -> Self {
        Self {
            is_live: false,
            media_parts: Vec::new(),
            duration: None,
            target_duration: None,
            init_segment: None,
            is_ended: false,
        }
    }
}

impl Default for MediaPart {
    fn default() -> Self {
        Self {
            uri: String::new(),
            media_segments: Vec::new(),
            duration: None,
        }
    }
}
