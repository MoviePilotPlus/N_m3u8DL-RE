using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Processor.HLS;

namespace N_m3u8DL_RE.Tests.Parser.Processor.HLS;

public class DefaultHLSKeyProcessorTests
{
    [Fact]
    public void Process_NonIdentitySkdKeyFormat_DoesNotFetchKeyOrMarkUnknown()
    {
        var processor = new DefaultHLSKeyProcessor();
        const string keyLine = "#EXT-X-KEY:METHOD=SAMPLE-AES,URI=\"skd://407ea39e7e610976c3f8a23c90ddbf57\",KEYFORMATVERSIONS=\"1\",KEYFORMAT=\"com.apple.streamingkeydelivery\"";

        var info = processor.Process(keyLine, "http://example.com/video.m3u8", "", new ParserConfig());

        Assert.Equal(EncryptMethod.SAMPLE_AES, info.Method);
        Assert.Null(info.Key);
    }
}
