use std::collections::HashMap;
use reqwest::{Client};
use reqwest::header::{HeaderMap, HeaderName, HeaderValue};
use crate::entity::stream::StreamSpec;

#[derive(Debug, Clone)]
pub struct StreamExtractor {
    client: Client,
    base_url: Option<String>,
    headers: HashMap<String, String>,
    extractor_type: ExtractorType,
    raw_files: HashMap<String, String>,
}

#[derive(Debug, Clone, PartialEq)]
pub enum ExtractorType {
    HTTP_LIVE,
    MPEG_DASH,
    MSS,
    UNKNOWN,
}

impl StreamExtractor {
    pub fn new(base_url: Option<String>, headers: HashMap<String, String>) -> Self {
        let client = Client::builder()
            .timeout(std::time::Duration::from_secs(100))
            .build()
            .unwrap();
        
        Self {
            client,
            base_url,
            headers,
            extractor_type: ExtractorType::UNKNOWN,
            raw_files: HashMap::new(),
        }
    }
    
    pub async fn load_source_from_url(&mut self, url: &str) -> Result<(), Box<dyn std::error::Error>> {
        // 构建HTTP请求头
        let mut headers = HeaderMap::new();
        for (key, value) in &self.headers {
            let header_name = HeaderName::try_from(key)?;
            let header_value = HeaderValue::try_from(value)?;
            headers.insert(header_name, header_value);
        }
        
        // 发送HTTP请求获取内容
        let response = self.client.get(url)
            .headers(headers)
            .send()
            .await?;
        
        let content = response.text().await?;
        
        // 检测流类型
        self.detect_extractor_type(&content, url);
        
        // 保存原始内容
        self.raw_files.insert("source.txt".to_string(), content);
        
        Ok(())
    }
    
    pub async fn extract_streams(&mut self) -> Result<Vec<StreamSpec>, Box<dyn std::error::Error>> {
        // 根据流类型选择对应的解析器
        match self.extractor_type {
            ExtractorType::HTTP_LIVE => {
                // 使用HLS解析器
                let hls_extractor = crate::parser::hls::HLSExtractor::new(self.base_url.clone(), self.headers.clone());
                hls_extractor.extract_streams(&self.raw_files.get("source.txt").unwrap())
            }
            ExtractorType::MPEG_DASH => {
                // 使用DASH解析器
                let dash_extractor = crate::parser::dash::DASHExtractor::new(self.base_url.clone(), self.headers.clone());
                dash_extractor.extract_streams(&self.raw_files.get("source.txt").unwrap())
            }
            ExtractorType::MSS => {
                // 使用MSS解析器
                let mss_extractor = crate::parser::mss::MSSExtractor::new(self.base_url.clone(), self.headers.clone());
                mss_extractor.extract_streams(&self.raw_files.get("source.txt").unwrap())
            }
            ExtractorType::UNKNOWN => {
                Err(Box::new(std::io::Error::new(std::io::ErrorKind::Other, "Unknown stream type")))
            }
        }
    }
    
    pub async fn fetch_playlist(&mut self, streams: &mut Vec<StreamSpec>) -> Result<(), Box<dyn std::error::Error>> {
        // 根据流类型获取播放列表
        for stream in streams {
            if let Some(_playlist) = &mut stream.playlist {
                // 这里应该实现获取播放列表的逻辑
                // 暂时留空
            }
        }
        Ok(())
    }
    
    fn detect_extractor_type(&mut self, content: &str, url: &str) {
        // 根据内容和URL检测流类型
        if content.contains("#EXTM3U") {
            self.extractor_type = ExtractorType::HTTP_LIVE;
        } else if content.contains("<MPD") {
            self.extractor_type = ExtractorType::MPEG_DASH;
        } else if content.contains("<SmoothStreamingMedia") {
            self.extractor_type = ExtractorType::MSS;
        } else if url.ends_with(".m3u8") {
            self.extractor_type = ExtractorType::HTTP_LIVE;
        } else if url.ends_with(".mpd") {
            self.extractor_type = ExtractorType::MPEG_DASH;
        } else if url.ends_with(".ism") {
            self.extractor_type = ExtractorType::MSS;
        } else {
            self.extractor_type = ExtractorType::UNKNOWN;
        }
    }
    
    pub fn get_raw_files(&self) -> &HashMap<String, String> {
        &self.raw_files
    }
    
    pub fn get_extractor_type(&self) -> ExtractorType {
        self.extractor_type.clone()
    }
}
