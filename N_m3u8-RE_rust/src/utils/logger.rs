use log::{Level, LevelFilter, Metadata, Record};
use std::fs::File;
use std::io::Write;
use std::sync::Mutex;

struct SimpleLogger {
    file: Option<Mutex<File>>,
    level: LevelFilter,
}

impl log::Log for SimpleLogger {
    fn enabled(&self, metadata: &Metadata) -> bool {
        metadata.level() <= self.level
    }

    fn log(&self, record: &Record) {
        if self.enabled(record.metadata()) {
            let msg = format!("[{}] {}", record.level(), record.args());
            println!("{}", msg);
            
            if let Some(file) = &self.file {
                let mut file = file.lock().unwrap();
                writeln!(file, "{}", msg).unwrap();
            }
        }
    }

    fn flush(&self) {
        if let Some(file) = &self.file {
            let mut file = file.lock().unwrap();
            file.flush().unwrap();
        }
    }
}

pub fn init_logger(log_file_path: Option<&str>, log_level: &str) {
    let level = match log_level {
        "DEBUG" => LevelFilter::Debug,
        "INFO" => LevelFilter::Info,
        "WARN" => LevelFilter::Warn,
        "ERROR" => LevelFilter::Error,
        "OFF" => LevelFilter::Off,
        _ => LevelFilter::Info,
    };
    
    let file = log_file_path.map(|path| {
        Mutex::new(File::create(path).unwrap())
    });
    
    let logger = SimpleLogger {
        file,
        level,
    };
    
    log::set_boxed_logger(Box::new(logger)).unwrap();
    log::set_max_level(level);
}
