using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Translumo.OCR.WindowsOCR;

namespace Translumo.OCR.PaddleOcrVl
{
    /// <summary>
    /// One-shot PaddleOCR-VL "Spotting:" call that returns recognized lines with pixel positions
    /// (used by instant image translation).
    /// </summary>
    public static class PaddleOcrVlClient
    {
        public const string RESULT_LANGUAGE_TAG = "PaddleOCR-VL";

        // Official spotting pre-processing: upscale small images (both sides under 1500 px) up to 2x,
        // within the spotting pixel budget of 2048 * 28 * 28
        private const int UPSCALE_THRESHOLD_PX = 1500;
        private const double MAX_UPSCALE = 2.0;
        private const double SPOTTING_MAX_PIXELS = 2048 * 28 * 28;
        // A full page takes several seconds; never wait less than this regardless of the OCR timeout setting
        private const int MIN_SPOTTING_TIMEOUT_SECONDS = 60;
        private const int MAX_ERROR_BODY_LENGTH = 300;

        /// <summary>
        /// Recognizes text and positions in <paramref name="image"/> (encoded bytes). Box coordinates are in
        /// the original image's pixels. Throws <see cref="PaddleOcrVlPositionsUnavailableException"/> when
        /// the server returns text without location tokens.
        /// </summary>
        public static Task<OcrRegionResult> SpotAsync(PaddleOcrVlConfiguration configuration, byte[] image,
            CancellationToken cancellationToken = default) =>
            SpotAsync(configuration, image, new HttpClientHandler(), cancellationToken);

        internal static async Task<OcrRegionResult> SpotAsync(PaddleOcrVlConfiguration configuration, byte[] image,
            HttpMessageHandler handler, CancellationToken cancellationToken = default)
        {
            var (png, width, height) = PrepareImage(image);

            using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(MIN_SPOTTING_TIMEOUT_SECONDS, configuration.TimeoutSeconds)));

            using var request = PaddleOcrVlProtocol.BuildSpottingRequest(configuration, png);
            using var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var excerpt = body.Length <= MAX_ERROR_BODY_LENGTH ? body : body[..MAX_ERROR_BODY_LENGTH] + "…";
                throw new HttpRequestException($"PaddleOCR-VL request failed: HTTP {(int)response.StatusCode}: {excerpt}");
            }

            var content = PaddleOcrVlProtocol.ReadContent(body);
            var lines = PaddleOcrVlProtocol.ParseSpotting(content, width, height);
            if (lines.Count == 0 && PaddleOcrVlProtocol.ParseLines(body).Length > 0
                                 && !PaddleOcrVlProtocol.HasLocationTokens(content))
            {
                throw new PaddleOcrVlPositionsUnavailableException();
            }

            return new OcrRegionResult
            {
                LanguageTag = RESULT_LANGUAGE_TAG,
                Lines = lines,
                ImageWidth = width,
                ImageHeight = height
            };
        }

        /// <summary>Decodes the image, upscales small ones, and returns PNG bytes plus the original size.</summary>
        internal static (byte[] Png, int Width, int Height) PrepareImage(byte[] image)
        {
            using var mat = Cv2.ImDecode(image, ImreadModes.Color);
            if (mat.Empty())
            {
                throw new ArgumentException("Captured image could not be decoded", nameof(image));
            }

            var scale = GetUpscaleFactor(mat.Width, mat.Height);
            if (scale <= 1.0)
            {
                Cv2.ImEncode(".png", mat, out var original);
                return (original, mat.Width, mat.Height);
            }

            using var resized = new Mat();
            Cv2.Resize(mat, resized, new OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Cubic);
            Cv2.ImEncode(".png", resized, out var upscaled);

            return (upscaled, mat.Width, mat.Height);
        }

        internal static double GetUpscaleFactor(int width, int height)
        {
            if (width >= UPSCALE_THRESHOLD_PX || height >= UPSCALE_THRESHOLD_PX)
            {
                return 1.0;
            }

            var withinBudget = Math.Sqrt(SPOTTING_MAX_PIXELS / ((double)width * height));

            return Math.Max(1.0, Math.Min(MAX_UPSCALE, withinBudget));
        }
    }
}
