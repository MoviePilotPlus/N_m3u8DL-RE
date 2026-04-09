use std::collections::HashMap;
use std::path::Path;
use std::sync::Arc;
use tokio::sync::Semaphore;
use tokio::io::AsyncWriteExt;
use indicatif::{ProgressBar, ProgressStyle};
use crate::entity::stream::StreamSpec;
use crate::downloader::http::HttpUtil;

#[derive(Debug, Clone)]
pub struct SimpleDownloadManager {
    http_util: HttpUtil,
    thread_count: usize,
    tmp_dir: String,
}

impl SimpleDownloadManager {
    pub fn new(headers: HashMap<String, String>, thread_count: usize, tmp_dir: String) -> Self {
        Self {
            http_util: HttpUtil::new(headers),
            thread_count,
            tmp_dir,
        }
    }
    
    pub async fn start_download(&self, streams: &[StreamSpec]) -> Result<bool, Box<dyn std::error::Error + Send + Sync>> {
        // 创建临时目录
        tokio::fs::create_dir_all(&self.tmp_dir).await?;
        
        // 为每个流创建下载任务
        let semaphore = Arc::new(Semaphore::new(self.thread_count));
        let mut tasks = Vec::new();
        
        for stream in streams {
            let stream = stream.clone();
            let http_util = self.http_util.clone();
            let tmp_dir = self.tmp_dir.clone();
            let semaphore = semaphore.clone();
            
            tasks.push(tokio::spawn(async move {
                let permit = semaphore.acquire_owned().await.unwrap();
                let _permit = permit;
                SimpleDownloadManager::download_stream(&http_util, &stream, &tmp_dir).await
            }));
        }
        
        // 等待所有下载任务完成
        let mut all_success = true;
        for task in tasks {
            if let Err(e) = task.await? {
                eprintln!("下载失败: {:?}", e);
                all_success = false;
            }
        }
        
        Ok(all_success)
    }
    
    async fn download_stream(http_util: &HttpUtil, stream: &StreamSpec, tmp_dir: &str) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
        // 创建流的临时目录
        let stream_dir = Path::new(tmp_dir).join(&stream.id);
        tokio::fs::create_dir_all(&stream_dir).await?;
        
        // 下载分片
        if let Some(playlist) = &stream.playlist {
            // 下载初始化片段
            if let Some(init_segment) = &playlist.init_segment {
                let init_path = stream_dir.join("init.mp4");
                http_util.download_segment(init_segment, init_path.to_str().unwrap()).await?;
            }
            
            let total_segments: usize = playlist.media_parts.iter()
                .map(|part| part.media_segments.len())
                .sum();
            
            let pb = ProgressBar::new(total_segments as u64);
            pb.set_style(ProgressStyle::default_bar()
                .template("{spinner:.green} [{elapsed_precise}] [{bar:40.cyan/blue}] {pos}/{len} ({eta}) {msg}")?
                .progress_chars("█▓▒░"));
            
            pb.set_message(format!("下载 {}...", stream.id));
            
            let mut segment_index = 0;
            for (part_index, part) in playlist.media_parts.iter().enumerate() {
                for (segment_index_in_part, segment) in part.media_segments.iter().enumerate() {
                    let segment_path = stream_dir.join(format!("segment_{}_{}.ts", part_index, segment_index_in_part));
                    
                    // 下载分片（带重试）
                    let mut retries = 3;
                    loop {
                        match http_util.download_segment(&segment.uri, segment_path.to_str().unwrap()).await {
                            Ok(_) => break,
                            Err(e) => {
                                retries -= 1;
                                if retries == 0 {
                                    return Err(e);
                                }
                                eprintln!("下载失败，重试 ({}/{})...: {:?}", 3 - retries, 3, e);
                                tokio::time::sleep(tokio::time::Duration::from_secs(1)).await;
                            }
                        }
                    }
                    
                    pb.inc(1);
                    segment_index += 1;
                }
            }
            
            pb.finish_with_message(format!("{} 下载完成", stream.id));
            
            // 合并分片
            Self::merge_segments(&stream_dir, &stream.id).await?;
        }
        
        Ok(())
    }
    
    async fn merge_segments(stream_dir: &Path, stream_id: &str) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
        let output_path = stream_dir.join(format!("{}.ts", stream_id));
        let mut output_file = tokio::fs::File::create(&output_path).await?;
        
        // 先写入初始化片段
        let init_path = stream_dir.join("init.mp4");
        if init_path.exists() {
            let init_data = tokio::fs::read(&init_path).await?;
            output_file.write_all(&init_data).await?;
        }
        
        // 写入所有分片
        let mut segment_files = Vec::new();
        let mut dir = tokio::fs::read_dir(stream_dir).await?;
        while let Some(entry_result) = dir.next_entry().await? {
            let path = entry_result.path();
            if path.is_file() {
                let filename = path.file_name().unwrap().to_str().unwrap();
                if filename.starts_with("segment_") {
                    segment_files.push(path);
                }
            }
        }
        
        // 按文件名排序
        segment_files.sort();
        
        // 写入所有分片数据
        for segment_path in segment_files {
            let segment_data = tokio::fs::read(&segment_path).await?;
            output_file.write_all(&segment_data).await?;
        }
        
        Ok(())
    }
}
