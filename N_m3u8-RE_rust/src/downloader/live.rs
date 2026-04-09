use std::collections::HashMap;
use std::path::Path;
use std::sync::Arc;
use std::time::Duration;
use tokio::sync::Semaphore;
use tokio::time::sleep;
use tokio::io::AsyncWriteExt;
use indicatif::{ProgressBar, ProgressStyle};
use crate::entity::stream::StreamSpec;
use crate::downloader::http::HttpUtil;

#[derive(Debug, Clone)]
pub struct LiveDownloadManager {
    http_util: HttpUtil,
    thread_count: usize,
    tmp_dir: String,
    record_limit: Option<Duration>,
    wait_time: Option<u64>,
    take_count: usize,
}

impl LiveDownloadManager {
    pub fn new(
        headers: HashMap<String, String>, 
        thread_count: usize, 
        tmp_dir: String,
        record_limit: Option<Duration>,
        wait_time: Option<u64>,
        take_count: usize
    ) -> Self {
        Self {
            http_util: HttpUtil::new(headers),
            thread_count,
            tmp_dir,
            record_limit,
            wait_time,
            take_count,
        }
    }
    
    pub async fn start_record(&self, streams: &[StreamSpec]) -> Result<bool, Box<dyn std::error::Error + Send + Sync>> {
        // 创建临时目录
        tokio::fs::create_dir_all(&self.tmp_dir).await?;
        
        // 为每个流创建下载任务
        let semaphore = Arc::new(Semaphore::new(self.thread_count));
        let mut tasks = Vec::new();
        
        for stream in streams {
            let stream = stream.clone();
            let http_util = self.http_util.clone();
            let tmp_dir = self.tmp_dir.clone();
            let record_limit = self.record_limit;
            let wait_time = self.wait_time;
            let take_count = self.take_count;
            let semaphore = semaphore.clone();
            
            tasks.push(tokio::spawn(async move {
                let permit = semaphore.acquire_owned().await.unwrap();
                let _permit = permit;
                LiveDownloadManager::record_stream(
                    &http_util, 
                    &stream, 
                    &tmp_dir,
                    record_limit,
                    wait_time,
                    take_count
                ).await
            }));
        }
        
        // 等待所有下载任务完成
        let mut all_success = true;
        for task in tasks {
            if let Err(e) = task.await? {
                eprintln!("录制失败: {:?}", e);
                all_success = false;
            }
        }
        
        Ok(all_success)
    }
    
    async fn record_stream(
        http_util: &HttpUtil, 
        stream: &StreamSpec, 
        tmp_dir: &str,
        record_limit: Option<Duration>,
        wait_time: Option<u64>,
        take_count: usize
    ) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
        // 创建流的临时目录
        let stream_dir = Path::new(tmp_dir).join(&stream.id);
        tokio::fs::create_dir_all(&stream_dir).await?;
        
        // 开始录制
        let start_time = std::time::Instant::now();
        let pb = ProgressBar::new_spinner();
        pb.set_style(ProgressStyle::default_spinner()
            .template("{spinner:.green} [{elapsed_precise}] {msg}")?
            .tick_strings(&["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"]));
        pb.set_message(format!("录制 {}...", stream.id).as_str());
        
        let mut segment_index = 0;
        let mut downloaded_segments = std::collections::HashSet::new();
        
        loop {
            // 检查录制时间限制
            if let Some(limit) = record_limit {
                if start_time.elapsed() >= limit {
                    break;
                }
            }
            
            // 获取直播流的最新分片
            if let Some(playlist) = &stream.playlist {
                for part in &playlist.media_parts {
                    for segment in &part.media_segments {
                        // 只下载未下载过的分片
                        if !downloaded_segments.contains(&segment.uri) {
                            let segment_path = stream_dir.join(format!("segment_{}.ts", segment_index));
                            
                            // 下载分片（带重试）
                            let mut retries = 3;
                            loop {
                                match http_util.download_segment(&segment.uri, segment_path.to_str().unwrap()).await {
                                    Ok(_) => {
                                        downloaded_segments.insert(segment.uri.clone());
                                        break;
                                    }
                                    Err(e) => {
                                        retries -= 1;
                                        if retries == 0 {
                                            eprintln!("下载分片失败: {:?}", e);
                                            break;
                                        }
                                        eprintln!("下载失败，重试 ({}/{})...: {:?}", 3 - retries, 3, e);
                                        sleep(Duration::from_secs(1)).await;
                                    }
                                }
                            }
                            
                            segment_index += 1;
                            
                            // 限制每轮下载的分片数量
                            if segment_index % take_count == 0 {
                                break;
                            }
                        }
                    }
                }
            }
            
            // 实时合并分片
            Self::live_merge(&stream_dir, &stream.id).await?;
            
            // 等待下一轮
            let wait = wait_time.unwrap_or(5);
            sleep(Duration::from_secs(wait)).await;
        }
        
        pb.finish_with_message(format!("{} 录制完成", stream.id).as_str());
        
        Ok(())
    }
    
    async fn live_merge(stream_dir: &Path, stream_id: &str) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
        let output_path = stream_dir.join(format!("{}_live.ts", stream_id));
        let mut output_file = tokio::fs::File::create(&output_path).await?;
        
        // 写入所有分片
        let mut segment_files = Vec::new();
        let mut dir = tokio::fs::read_dir(stream_dir).await?;
        while let Some(entry_result) = dir.next_entry().await? {
            let path = entry_result.path();
            if path.is_file() {
                let filename = path.file_name().unwrap().to_str().unwrap();
                if filename.starts_with("segment_") && filename.ends_with(".ts") {
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
