use regex::Regex;
use crate::entity::stream::StreamSpec;

pub fn filter_streams(streams: &[StreamSpec], filter: Option<&str>) -> Vec<StreamSpec> {
    if let Some(filter_str) = filter {
        // 解析过滤条件
        let filters = parse_filter(filter_str);
        streams.iter()
            .filter(|stream| matches_filter(stream, &filters))
            .cloned()
            .collect()
    } else {
        streams.to_vec()
    }
}

pub fn drop_streams(streams: &[StreamSpec], filter: Option<&str>) -> Vec<StreamSpec> {
    if let Some(filter_str) = filter {
        // 解析过滤条件
        let filters = parse_filter(filter_str);
        streams.iter()
            .filter(|stream| !matches_filter(stream, &filters))
            .cloned()
            .collect()
    } else {
        streams.to_vec()
    }
}

fn parse_filter(filter_str: &str) -> Vec<(String, String)> {
    filter_str.split(':')
        .filter_map(|part| {
            if let Some((key, value)) = part.split_once('=') {
                Some((key.to_string(), value.to_string()))
            } else {
                None
            }
        })
        .collect()
}

fn matches_filter(stream: &StreamSpec, filters: &[(String, String)]) -> bool {
    for (key, value) in filters {
        match key.as_str() {
            "id" => {
                if let Ok(re) = Regex::new(value) {
                    if !re.is_match(&stream.id) {
                        return false;
                    }
                }
            }
            "lang" => {
                if let Some(lang) = &stream.language {
                    if let Ok(re) = Regex::new(value) {
                        if !re.is_match(lang) {
                            return false;
                        }
                    }
                } else {
                    return false;
                }
            }
            "name" => {
                if let Some(name) = &stream.name {
                    if let Ok(re) = Regex::new(value) {
                        if !re.is_match(name) {
                            return false;
                        }
                    }
                } else {
                    return false;
                }
            }
            "codecs" => {
                if let Ok(re) = Regex::new(value) {
                    if !re.is_match(&stream.codecs) {
                        return false;
                    }
                }
            }
            "res" => {
                if let Some(res) = &stream.resolution {
                    if let Ok(re) = Regex::new(value) {
                        if !re.is_match(res) {
                            return false;
                        }
                    }
                } else {
                    return false;
                }
            }
            "frame" => {
                if let Some(frame_rate) = stream.frame_rate {
                    if let Ok(re) = Regex::new(value) {
                        if !re.is_match(&frame_rate.to_string()) {
                            return false;
                        }
                    }
                } else {
                    return false;
                }
            }
            _ => {}
        }
    }
    true
}
