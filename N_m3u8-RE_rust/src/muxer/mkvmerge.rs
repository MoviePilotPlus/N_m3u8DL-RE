use std::process::Command;

#[derive(Debug, Clone)]
pub struct MKVMergeMuxer {
    mkvmerge_path: String,
}

impl MKVMergeMuxer {
    pub fn new(mkvmerge_path: Option<String>) -> Self {
        let path = mkvmerge_path.unwrap_or_else(|| {
            // 尝试在系统路径中查找mkvmerge
            if let Ok(output) = Command::new("which").arg("mkvmerge").output() {
                if output.status.success() {
                    return String::from_utf8_lossy(&output.stdout).trim().to_string();
                }
            }
            "mkvmerge".to_string()
        });
        
        Self {
            mkvmerge_path: path,
        }
    }
    
    pub fn mux(&self, video_path: &str, audio_path: &str, output_path: &str) -> Result<(), Box<dyn std::error::Error>> {
        let output = Command::new(&self.mkvmerge_path)
            .arg("-o")
            .arg(output_path)
            .arg(video_path)
            .arg(audio_path)
            .output()?;
        
        if !output.status.success() {
            return Err(Box::new(std::io::Error::new(
                std::io::ErrorKind::Other,
                format!("MKVMerge混流失败: {}", String::from_utf8_lossy(&output.stderr))
            )));
        }
        
        Ok(())
    }
}
