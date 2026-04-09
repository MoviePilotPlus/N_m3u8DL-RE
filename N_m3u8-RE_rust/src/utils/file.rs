use std::fs;
use std::path::Path;

pub fn create_dir_all(path: &str) -> Result<(), Box<dyn std::error::Error>> {
    fs::create_dir_all(path)?;
    Ok(())
}

pub fn remove_dir_all(path: &str) -> Result<(), Box<dyn std::error::Error>> {
    fs::remove_dir_all(path)?;
    Ok(())
}

pub fn write_file(path: &str, content: &str) -> Result<(), Box<dyn std::error::Error>> {
    fs::write(path, content)?;
    Ok(())
}

pub fn read_file(path: &str) -> Result<String, Box<dyn std::error::Error>> {
    let content = fs::read_to_string(path)?;
    Ok(content)
}

pub fn file_exists(path: &str) -> bool {
    Path::new(path).exists()
}

pub fn is_dir(path: &str) -> bool {
    Path::new(path).is_dir()
}
