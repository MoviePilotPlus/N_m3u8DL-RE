use std::collections::HashMap;
use reqwest::{Client};
use reqwest::header::{HeaderMap, HeaderName, HeaderValue};
use tokio::io::AsyncWriteExt;
use futures_util::stream::StreamExt;
use bytes::Bytes;

#[derive(Debug, Clone)]
pub struct HttpUtil {
    client: Client,
    headers: HashMap<String, String>,
}

impl HttpUtil {
    pub fn new(headers: HashMap<String, String>) -> Self {
        let client = Client::builder()
            .timeout(std::time::Duration::from_secs(100))
            .build()
            .unwrap();
        
        Self {
            client,
            headers,
        }
    }
    
    pub async fn get(&self, url: &str) -> Result<String, Box<dyn std::error::Error + Send + Sync>> {
        let mut headers = HeaderMap::new();
        for (key, value) in &self.headers {
            let header_name = HeaderName::try_from(key)?;
            let header_value = HeaderValue::try_from(value)?;
            headers.insert(header_name, header_value);
        }
        
        let response = self.client.get(url)
            .headers(headers)
            .send()
            .await?;
        
        let content = response.text().await?;
        Ok(content)
    }
    
    pub async fn download_segment(&self, url: &str, output_path: &str) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
        let mut headers = HeaderMap::new();
        for (key, value) in &self.headers {
            let header_name = HeaderName::try_from(key)?;
            let header_value = HeaderValue::try_from(value)?;
            headers.insert(header_name, header_value);
        }
        
        let response = self.client.get(url)
            .headers(headers)
            .send()
            .await?;
        
        let mut file = tokio::fs::File::create(output_path).await?;
        let mut stream = response.bytes_stream();
        
        while let Some(chunk_result) = stream.next().await {
            let chunk: Bytes = chunk_result?;
            file.write_all(&chunk).await?;
        }
        
        Ok(())
    }
    
    pub async fn head(&self, url: &str) -> Result<reqwest::Response, Box<dyn std::error::Error>> {
        let mut headers = HeaderMap::new();
        for (key, value) in &self.headers {
            let header_name = HeaderName::try_from(key)?;
            let header_value = HeaderValue::try_from(value)?;
            headers.insert(header_name, header_value);
        }
        
        let response = self.client.head(url)
            .headers(headers)
            .send()
            .await?;
        
        Ok(response)
    }
}
