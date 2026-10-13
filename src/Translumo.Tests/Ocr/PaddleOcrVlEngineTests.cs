using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Translumo.Infrastructure.Language;
using Translumo.OCR.PaddleOcrVl;
using Xunit;

namespace Translumo.Tests.Ocr
{
    public class PaddleOcrVlEngineTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;

            public HttpRequestMessage? LastRequest { get; private set; }
            public string? LastRequestBody { get; private set; }

            public StubHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body = body;
            }

            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                LastRequestBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

                return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(Send(request, cancellationToken));
        }

        private static byte[] CreateTiff()
        {
            using var mat = new Mat(8, 16, MatType.CV_8UC3, Scalar.White);
            Cv2.ImEncode(".tif", mat, out var tiff);

            return tiff;
        }

        [Fact]
        public void GetTextLines_posts_png_to_server_and_returns_lines()
        {
            var handler = new StubHandler(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"Line 1\\nLine 2\"}}]}");
            using var engine = new PaddleOcrVlEngine(Languages.English, new PaddleOcrVlConfiguration(), handler);

            var lines = engine.GetTextLines(CreateTiff());

            Assert.Equal(new[] { "Line 1", "Line 2" }, lines);
            Assert.Equal("http://localhost:8000/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
            Assert.Contains("data:image/png;base64,iVBORw0KGgo", handler.LastRequestBody);
        }

        [Fact]
        public void GetTextLines_returns_grouped_blocks_and_engine_preserves_line_breaks()
        {
            var handler = new StubHandler(HttpStatusCode.OK,
                "{\"choices\":[{\"message\":{\"content\":\"Aria\\nWe must reach the gate before\\nthe sun sets.\\n[E] Continue\"}}]}");
            using var engine = new PaddleOcrVlEngine(Languages.English, new PaddleOcrVlConfiguration(), handler);

            var blocks = engine.GetTextLines(CreateTiff());

            Assert.Equal(new[] { "Aria", "We must reach the gate before the sun sets.", "[E] Continue" }, blocks);
            Assert.True(((Translumo.OCR.IOCREngine)engine).PreservesLineBreaks);
            Assert.Contains("\"Spotting:\"", handler.LastRequestBody);
        }

        [Fact]
        public void GetTextLines_throws_with_status_code_on_server_error()
        {
            var handler = new StubHandler(HttpStatusCode.InternalServerError, "model crashed");
            using var engine = new PaddleOcrVlEngine(Languages.English, new PaddleOcrVlConfiguration(), handler);

            var ex = Assert.Throws<HttpRequestException>(() => engine.GetTextLines(CreateTiff()));

            Assert.Contains("500", ex.Message);
            Assert.Contains("model crashed", ex.Message);
        }

        [Fact]
        public void GetTextLines_uses_latest_configuration()
        {
            var handler = new StubHandler(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"x\"}}]}");
            var cfg = new PaddleOcrVlConfiguration();
            using var engine = new PaddleOcrVlEngine(Languages.English, cfg, handler);

            cfg.Endpoint = "http://gpu-box:9000/v1";
            engine.GetTextLines(CreateTiff());
            Assert.Equal("http://gpu-box:9000/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());

            engine.UseConfiguration(new PaddleOcrVlConfiguration { Endpoint = "http://other/v1" });
            engine.GetTextLines(CreateTiff());
            Assert.Equal("http://other/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
        }

        [Fact]
        public void ToPng_converts_tiff_and_rejects_garbage()
        {
            var png = PaddleOcrVlProtocol.ToPng(CreateTiff());

            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
            Assert.Throws<ArgumentException>(() => PaddleOcrVlProtocol.ToPng(new byte[] { 1, 2, 3, 4 }));
        }
    }
}
