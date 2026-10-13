using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Translumo.Infrastructure.Language;

namespace Translumo.OCR.PaddleOcrVl
{
    /// <summary>
    /// PaddleOCR-VL vision-language OCR via an OpenAI-compatible server. Settings are read on every request,
    /// so endpoint/model/key edits apply without recreating the engine.
    /// </summary>
    public class PaddleOcrVlEngine : IOCREngine, IDisposable
    {
        private const int MAX_ERROR_BODY_LENGTH = 300;

        // Lowest priority: a cheaper engine (if enabled) gates the capture loop and this one runs only on full iterations
        public byte PrimaryPriority => 0;
        public bool SecondaryPrimaryCheck => false;
        public int Confidence => 10;
        // Each returned element is a text block; keep them on separate lines instead of joining into one sentence
        public bool PreservesLineBreaks => true;
        public Languages DetectionLanguage { get; }

        private PaddleOcrVlConfiguration _configuration;

        private readonly bool _asian;
        private readonly HttpClient _httpClient;

        public PaddleOcrVlEngine(LanguageDescriptor languageDescriptor, PaddleOcrVlConfiguration configuration)
            : this(languageDescriptor.Language, configuration, new HttpClientHandler(), languageDescriptor.Asian)
        {
        }

        internal PaddleOcrVlEngine(Languages detectionLanguage, PaddleOcrVlConfiguration configuration, HttpMessageHandler handler,
            bool asian = false)
        {
            DetectionLanguage = detectionLanguage;
            _configuration = configuration;
            _asian = asian;
            // Per-request timeout is applied via CancellationToken so TimeoutSeconds changes take effect immediately
            _httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        /// <summary>Points the engine at the current configuration instance (it is replaced when settings are loaded).</summary>
        public void UseConfiguration(PaddleOcrVlConfiguration configuration)
        {
            Volatile.Write(ref _configuration, configuration);
        }

        public string[] GetTextLines(byte[] image)
        {
            var configuration = Volatile.Read(ref _configuration);
            var png = PaddleOcrVlProtocol.ToPng(image);

            using var request = PaddleOcrVlProtocol.BuildChatRequest(configuration, png);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, configuration.TimeoutSeconds)));
            using var response = SendOrThrow(request, configuration, cts.Token);

            var lines = PaddleOcrVlProtocol.ParseLines(response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult());

            return TextBlockGrouper.Group(lines, _asian);
        }

        /// <summary>Checks that the server is reachable and serves the configured model.</summary>
        public static async Task TestConnectionAsync(PaddleOcrVlConfiguration configuration, CancellationToken cancellationToken = default)
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, configuration.TimeoutSeconds)) };
            using var request = PaddleOcrVlProtocol.BuildModelsRequest(configuration);
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Truncate(body)}");
            }

            var models = PaddleOcrVlProtocol.ParseModelIds(body);
            if (models.Count > 0 && !models.Contains(configuration.ModelName?.Trim()))
            {
                throw new InvalidOperationException(
                    $"Model '{configuration.ModelName}' is not served. Available: {string.Join(", ", models)}");
            }
        }

        private HttpResponseMessage SendOrThrow(HttpRequestMessage request, PaddleOcrVlConfiguration configuration, CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            try
            {
                response = _httpClient.Send(request, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                throw new TimeoutException($"PaddleOCR-VL did not respond within {configuration.TimeoutSeconds}s", ex);
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            using (response)
            {
                var body = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                throw new HttpRequestException($"PaddleOCR-VL request failed: HTTP {(int)response.StatusCode}: {Truncate(body)}");
            }
        }

        private static string Truncate(string text) =>
            text.Length <= MAX_ERROR_BODY_LENGTH ? text : text[..MAX_ERROR_BODY_LENGTH] + "…";

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
