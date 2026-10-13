using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Translumo.OCR.PaddleOcrVl;
using Xunit;

namespace Translumo.Tests.Ocr
{
    public class PaddleOcrVlProtocolTests
    {
        [Fact]
        public void Request_json_has_model_png_data_uri_ocr_prompt_and_zero_temperature()
        {
            var json = PaddleOcrVlProtocol.BuildRequestJson("PaddlePaddle/PaddleOCR-VL", new byte[] { 1, 2, 3 });

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("PaddlePaddle/PaddleOCR-VL", root.GetProperty("model").GetString());
            Assert.Equal(0, root.GetProperty("temperature").GetInt32());
            Assert.True(root.GetProperty("max_tokens").GetInt32() > 0);

            var messages = root.GetProperty("messages").EnumerateArray().ToArray();
            Assert.Single(messages);
            Assert.Equal("user", messages[0].GetProperty("role").GetString());

            var content = messages[0].GetProperty("content").EnumerateArray().ToArray();
            Assert.Equal("image_url", content[0].GetProperty("type").GetString());
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                content[0].GetProperty("image_url").GetProperty("url").GetString());
            Assert.Equal("text", content[1].GetProperty("type").GetString());
            Assert.Equal("OCR:", content[1].GetProperty("text").GetString());
        }

        [Theory]
        [InlineData("http://localhost:8000/v1", "http://localhost:8000/v1/chat/completions")]
        [InlineData(" http://localhost:8000/v1/ ", "http://localhost:8000/v1/chat/completions")]
        [InlineData("http://host/v1/chat/completions", "http://host/v1/chat/completions")]
        public void Chat_request_targets_chat_completions(string endpoint, string expectedUrl)
        {
            var cfg = new PaddleOcrVlConfiguration { Endpoint = endpoint };

            using var request = PaddleOcrVlProtocol.BuildChatRequest(cfg, new byte[] { 1 });

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(expectedUrl, request.RequestUri!.ToString());
        }

        [Fact]
        public void Bearer_header_is_sent_only_when_api_key_is_set()
        {
            using var withoutKey = PaddleOcrVlProtocol.BuildChatRequest(new PaddleOcrVlConfiguration(), new byte[] { 1 });
            using var withKey = PaddleOcrVlProtocol.BuildChatRequest(new PaddleOcrVlConfiguration { ApiKey = " secret " }, new byte[] { 1 });

            Assert.Null(withoutKey.Headers.Authorization);
            Assert.Equal("Bearer", withKey.Headers.Authorization!.Scheme);
            Assert.Equal("secret", withKey.Headers.Authorization.Parameter);
        }

        [Fact]
        public void ParseLines_splits_crlf_trims_and_drops_blank_lines()
        {
            const string json = "{\"choices\":[{\"message\":{\"content\":\"  Hello world \\r\\n\\r\\nSecond line\\n\"}}]}";

            var lines = PaddleOcrVlProtocol.ParseLines(json);

            Assert.Equal(new[] { "Hello world", "Second line" }, lines);
        }

        [Theory]
        [InlineData("{\"choices\":[]}")]
        [InlineData("{}")]
        [InlineData("{\"choices\":[{\"message\":{}}]}")]
        [InlineData("{\"choices\":[{\"message\":{\"content\":null}}]}")]
        public void ParseLines_returns_empty_when_there_is_no_content(string json)
        {
            Assert.Empty(PaddleOcrVlProtocol.ParseLines(json));
        }

        [Fact]
        public void ParseLines_strips_leaked_location_tokens()
        {
            const string json = "{\"choices\":[{\"message\":{\"content\":\"Aria<|LOC_12|><|LOC_40|>\\nHP 120/150<|LOC_801|>\"}}]}";

            Assert.Equal(new[] { "Aria", "HP 120/150" }, PaddleOcrVlProtocol.ParseLines(json));
        }

        [Theory]
        [InlineData(true, "Spotting:")]
        [InlineData(false, "OCR:")]
        public void Chat_request_prompt_follows_detect_lines_setting(bool detectLines, string expectedPrompt)
        {
            var cfg = new PaddleOcrVlConfiguration { DetectLines = detectLines };

            using var request = PaddleOcrVlProtocol.BuildChatRequest(cfg, new byte[] { 1 });
            using var doc = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

            var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content")[1];
            Assert.Equal(expectedPrompt, content.GetProperty("text").GetString());
        }

        [Fact]
        public void Detect_lines_is_on_by_default()
        {
            Assert.True(new PaddleOcrVlConfiguration().DetectLines);
        }

        [Fact]
        public void ParseModelIds_reads_openai_models_list()
        {
            const string json = "{\"object\":\"list\",\"data\":[{\"id\":\"PaddlePaddle/PaddleOCR-VL\"},{\"id\":\"other\"}]}";

            Assert.Equal(new[] { "PaddlePaddle/PaddleOCR-VL", "other" }, PaddleOcrVlProtocol.ParseModelIds(json));
        }
    }
}
