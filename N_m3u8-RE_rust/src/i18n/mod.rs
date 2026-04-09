pub mod en;
pub mod zh_cn;
pub mod zh_tw;

use std::collections::HashMap;

#[derive(Debug, Clone)]
pub struct I18n {
    translations: HashMap<String, String>,
}

impl I18n {
    pub fn new(language: &str) -> Self {
        let translations = match language {
            "en-US" => en::TRANSLATIONS.clone(),
            "zh-CN" => zh_cn::TRANSLATIONS.clone(),
            "zh-TW" => zh_tw::TRANSLATIONS.clone(),
            _ => en::TRANSLATIONS.clone(),
        };
        
        Self {
            translations,
        }
    }
    
    pub fn get<'a>(&'a self, key: &'a str) -> &'a str {
        self.translations.get(key).map_or(key, |v| v.as_str())
    }
}
