use std::process::Command;

#[derive(Debug, Clone)]
pub struct FFmpegMuxer {
    ffmpeg_path: String,
}

impl FFmpegMuxer {
    pub fn new(ffmpeg_path: Option<String>) -> Self {
        let path = ffmpeg_path.unwrap_or_else(|| {
            // 尝试在系统路径中查找ffmpeg
            if let Ok(output) = Command::new("which").arg("ffmpeg").output() {
                if output.status.success() {
                    return String::from_utf8_lossy(&output.stdout).trim().to_string();
                }
            }
            "ffmpeg".to_string()
        });
        
        Self {
            ffmpeg_path: path,
        }
    }
    
    pub fn mux(&self, video_path: Option<&str>, audio_paths: &[&str], subtitle_paths: &[&str], output_path: &str) -> Result<(), Box<dyn std::error::Error>> {
        let mut command = Command::new(&self.ffmpeg_path);
        
        // 添加视频输入
        if let Some(video) = video_path {
            command.arg("-i").arg(video);
        }
        
        // 添加音频输入
        for audio in audio_paths {
            command.arg("-i").arg(audio);
        }
        
        // 添加字幕输入
        for subtitle in subtitle_paths {
            command.arg("-i").arg(subtitle);
        }
        
        // 设置编码选项
        command.arg("-c").arg("copy");
        
        // 对于字幕，可能需要指定编码
        if !subtitle_paths.is_empty() {
            command.arg("-c:s").arg("copy");
        }
        
        // 设置输出路径
        command.arg(output_path);
        
        let output = command.output()?;
        
        if !output.status.success() {
            return Err(Box::new(std::io::Error::new(
                std::io::ErrorKind::Other,
                format!("FFmpeg混流失败: {}", String::from_utf8_lossy(&output.stderr))
            )));
        }
        
        Ok(())
    }
    
    pub fn mux_with_options(&self, inputs: &[&str], output_path: &str, options: &[&str]) -> Result<(), Box<dyn std::error::Error>> {
        let mut command = Command::new(&self.ffmpeg_path);
        
        // 添加所有输入
        for input in inputs {
            command.arg("-i").arg(input);
        }
        
        // 添加选项
        for option in options {
            command.arg(option);
        }
        
        // 设置输出路径
        command.arg(output_path);
        
        let output = command.output()?;
        
        if !output.status.success() {
            return Err(Box::new(std::io::Error::new(
                std::io::ErrorKind::Other,
                format!("FFmpeg混流失败: {}", String::from_utf8_lossy(&output.stderr))
            )));
        }
        
        Ok(())
    }
}
