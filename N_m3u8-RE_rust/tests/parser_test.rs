use n_m3u8_re_rust::parser::hls::HLSExtractor;
use n_m3u8_re_rust::parser::dash::DASHExtractor;
use n_m3u8_re_rust::parser::mss::MSSExtractor;
use std::collections::HashMap;

#[test]
fn test_hls_extractor() {
    let headers = HashMap::new();
    let extractor = HLSExtractor::new(None, headers);
    
    // 测试简单的HLS播放列表解析
    let hls_content = "#EXTM3U
#EXT-X-VERSION:3
#EXT-X-TARGETDURATION:10
#EXT-X-MEDIA-SEQUENCE:0
#EXTINF:10.0,
segment0.ts
#EXTINF:10.0,
segment1.ts
#EXT-X-ENDLIST";
    
    match extractor.extract_streams(hls_content) {
        Ok(streams) => {
            assert!(!streams.is_empty());
        }
        Err(e) => {
            panic!("HLS解析失败: {:?}", e);
        }
    }
}

#[test]
fn test_dash_extractor() {
    let headers = HashMap::new();
    let extractor = DASHExtractor::new(None, headers);
    
    // 测试简单的DASH MPD解析
    let dash_content = r#"<?xml version="1.0" encoding="UTF-8"?>
<MPD xmlns="urn:mpeg:dash:schema:mpd:2011" version="1" profiles="urn:mpeg:dash:profile:isoff-on-demand:2011">
    <Period>
        <AdaptationSet mimeType="video/mp4" codecs="avc1.64001e">
            <Representation id="1" bandwidth="2000000" width="1280" height="720">
                <SegmentTemplate initialization="init.mp4" media="segment_$Number$.m4s" startNumber="1" duration="2"/>
            </Representation>
        </AdaptationSet>
    </Period>
</MPD>"#;
    
    match extractor.extract_streams(dash_content) {
        Ok(streams) => {
            assert!(!streams.is_empty());
        }
        Err(e) => {
            panic!("DASH解析失败: {:?}", e);
        }
    }
}

#[test]
fn test_mss_extractor() {
    let headers = HashMap::new();
    let extractor = MSSExtractor::new(None, headers);
    
    // 测试简单的MSS manifest解析
    let mss_content = r#"<?xml version="1.0" encoding="UTF-8"?>
<SmoothStreamingMedia MajorVersion="2" MinorVersion="0" TimeScale="10000000" Duration="300000000">
    <StreamIndex Type="video" QualityLevels="1" Chunks="10">
        <QualityLevel Index="0" Bitrate="2000000" Width="1280" Height="720" CodecPrivateData="ABCD"/>
        <c t="0" d="30000000" r="1"/>
    </StreamIndex>
</SmoothStreamingMedia>"#;
    
    match extractor.extract_streams(mss_content) {
        Ok(streams) => {
            assert!(!streams.is_empty());
        }
        Err(e) => {
            panic!("MSS解析失败: {:?}", e);
        }
    }
}