use std::collections::HashMap;
use std::path::Path;
use std::time::Duration;
use tokio::runtime::Runtime;
use crate::commandline::parser::parse_args;
use crate::parser::extractor::StreamExtractor;
use crate::downloader::simple::SimpleDownloadManager;
use crate::downloader::live::LiveDownloadManager;

mod commandline;
mod parser;
mod downloader;
mod entity;
mod crypto;
mod muxer;
mod utils;
mod i18n;

fn main() {
    // 解析命令行参数
    let option = parse_args();
    
    // 初始化运行时
    let rt = Runtime::new().unwrap();
    rt.block_on(async {
        // 执行主要工作
        if let Err(e) = do_work(option).await {
            eprintln!("错误: {:?}", e);
            std::process::exit(1);
        }
    });
}

async fn do_work(option: commandline::options::MyOption) -> Result<(), Box<dyn std::error::Error>> {
    // 构建HTTP请求头
    let mut headers = HashMap::new();
    headers.insert("user-agent".to_string(), "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/78.0.3904.108 Safari/537.36".to_string());
    
    // 添加用户自定义的头
    for (key, value) in option.headers {
        headers.insert(key, value);
    }
    
    // 初始化流提取器
    let mut extractor = StreamExtractor::new(option.base_url, headers.clone());
    
    // 加载和解析媒体流
    println!("加载媒体流...");
    extractor.load_source_from_url(&option.input).await?;
    let streams = extractor.extract_streams().await?;
    
    // 显示流信息
    println!("找到 {} 个流:", streams.len());
    for (index, stream) in streams.iter().enumerate() {
        println!("{}. {} - {} - {}bps", index + 1, stream.id, stream.media_type.as_ref().unwrap_or(&entity::stream::MediaType::VIDEO), stream.bandwidth);
    }
    
    // 选择流（暂时选择第一个）
    let selected_streams = if streams.is_empty() {
        return Err(Box::new(std::io::Error::new(std::io::ErrorKind::Other, "没有找到可下载的流")));
    } else {
        vec![streams[0].clone()]
    };
    
    // 生成临时目录
    let tmp_dir = option.tmp_dir.unwrap_or_else(|| {
        let save_name = option.save_name.unwrap_or_else(|| "download".to_string());
        format!("{}/{}_temp", std::env::current_dir().unwrap().to_str().unwrap(), save_name)
    });
    
    // 下载配置
    let thread_count = option.thread_count;
    
    // 检查是否为直播流
    let is_live = selected_streams.iter().any(|s| s.playlist.as_ref().map(|p| p.is_live).unwrap_or(false));
    
    // 开始下载
        let result = if is_live && !option.live_perform_as_vod {
            // 直播下载
            let live_manager = LiveDownloadManager::new(
                headers,
                thread_count,
                tmp_dir,
                option.live_record_limit,
                option.live_wait_time,
                option.live_take_count
            );
            live_manager.start_record(&selected_streams).await
        } else {
            // 点播下载
            let simple_manager = SimpleDownloadManager::new(
                headers,
                thread_count,
                tmp_dir
            );
            simple_manager.start_download(&selected_streams).await
        };
        
        match result {
            Ok(success) => {
                if success {
                    println!("下载完成！");
                } else {
                    println!("下载失败！");
                    return Err(Box::new(std::io::Error::new(std::io::ErrorKind::Other, "下载失败")));
                }
            }
            Err(e) => {
                return Err(e);
            }
        }
    
    Ok(())
}
