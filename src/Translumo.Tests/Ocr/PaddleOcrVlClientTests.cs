using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Translumo.OCR.PaddleOcrVl;
using Xunit;

namespace Translumo.Tests.Ocr
{
    public class PaddleOcrVlClientTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _content;

            public string? LastRequestBody { get; private set; }

            public StubHandler(string content)
            {
                _content = content;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                var body = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = _content } } } });

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            }
        }

        private static byte[] CreatePng(int width, int height)
        {
            using var mat = new Mat(height, width, MatType.CV_8UC3, Scalar.White);
            Cv2.ImEncode(".png", mat, out var png);

            return png;
        }

        [Fact]
        public async Task SpotAsync_requests_spotting_with_special_tokens_and_maps_boxes_to_original_size()
        {
            var handler = new StubHandler("魚拾った～<|LOC_791|><|LOC_328|><|LOC_827|><|LOC_328|><|LOC_827|><|LOC_449|><|LOC_791|><|LOC_449|></s>");

            var result = await PaddleOcrVlClient.SpotAsync(new PaddleOcrVlConfiguration(), CreatePng(874, 1240), handler);

            Assert.Contains("\"Spotting:\"", handler.LastRequestBody);
            Assert.Contains("\"skip_special_tokens\":false", handler.LastRequestBody);
            Assert.Equal(PaddleOcrVlClient.RESULT_LANGUAGE_TAG, result.LanguageTag);
            Assert.Equal(874, result.ImageWidth);
            var line = Assert.Single(result.Lines);
            Assert.Equal("魚拾った～", line.Text);
            Assert.Equal(791 / 1000f * 874, line.Box.X, 2);
            Assert.Equal(328 / 1000f * 1240, line.Box.Y, 2);
        }

        [Fact]
        public async Task SpotAsync_throws_when_server_hides_positions()
        {
            var handler = new StubHandler("魚拾った～\n食えないのか～");

            await Assert.ThrowsAsync<PaddleOcrVlPositionsUnavailableException>(
                () => PaddleOcrVlClient.SpotAsync(new PaddleOcrVlConfiguration(), CreatePng(200, 100), handler));
        }

        [Fact]
        public async Task SpotAsync_returns_empty_result_when_nothing_is_recognized()
        {
            var result = await PaddleOcrVlClient.SpotAsync(new PaddleOcrVlConfiguration(), CreatePng(200, 100), new StubHandler("</s>"));

            Assert.Empty(result.Lines);
        }

        [Fact]
        public void Small_images_are_upscaled_within_the_pixel_budget()
        {
            Assert.Equal(2.0, PaddleOcrVlClient.GetUpscaleFactor(400, 300));
            Assert.Equal(1.0, PaddleOcrVlClient.GetUpscaleFactor(1600, 400));            // one side over the threshold
            var factor = PaddleOcrVlClient.GetUpscaleFactor(1200, 1200);                 // 2x would exceed the budget
            Assert.InRange(factor, 1.0, 1.06);

            var (png, width, height) = PaddleOcrVlClient.PrepareImage(CreatePng(400, 300));
            using var sent = Cv2.ImDecode(png, ImreadModes.Color);
            Assert.Equal((400, 300), (width, height));
            Assert.Equal((800, 600), (sent.Width, sent.Height));
        }
    }
}
