use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MediaSegment {
    pub uri: String,
    pub duration: f64,
    pub sequence: Option<u64>,
    pub encrypt_info: Option<EncryptInfo>,
    pub range: Option<String>,
    pub title: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct EncryptInfo {
    pub method: EncryptMethod,
    pub key_uri: Option<String>,
    pub key: Option<Vec<u8>>,
    pub iv: Option<Vec<u8>>,
    pub kid: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub enum EncryptMethod {
    NONE,
    AES_128,
    AES_128_ECB,
    CENC,
    CHACHA20,
    SAMPLE_AES,
    SAMPLE_AES_CTR,
    UNKNOWN,
}

impl Default for MediaSegment {
    fn default() -> Self {
        Self {
            uri: String::new(),
            duration: 0.0,
            sequence: None,
            encrypt_info: None,
            range: None,
            title: None,
        }
    }
}

impl Default for EncryptInfo {
    fn default() -> Self {
        Self {
            method: EncryptMethod::NONE,
            key_uri: None,
            key: None,
            iv: None,
            kid: None,
        }
    }
}
