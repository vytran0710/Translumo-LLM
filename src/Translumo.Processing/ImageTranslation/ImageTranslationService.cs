using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Translumo.Infrastructure.Language;
using Translumo.OCR.Configuration;
using Translumo.OCR.PaddleOcrVl;
using Translumo.OCR.WindowsOCR;
using Translumo.Translation;
using Translumo.Translation.Configuration;
using Translumo.Translation.Google;
using Translumo.Translation.Llm;

namespace Translumo.Processing.ImageTranslation
{
    /// <summary>
    /// Orchestrates the instant image-translation ("Google Lens") flow: positional OCR of a captured region,
    /// grouping into text blocks (e.g. one per speech bubble), and translation of all blocks.
    ///
    /// Recognition: PaddleOCR-VL "Spotting:" (text + positions) when that engine is enabled, otherwise
    /// Windows OCR with source-language auto-detect. If PaddleOCR-VL fails or its server hides positions,
    /// Windows OCR is used and <see cref="ImageTranslationResult.Notice"/> explains why.
    ///
    /// Translation follows the global Translation settings:
    /// - LLM: all blocks are translated in ONE request (source auto-detected per block, blocks give each other
    ///   context); if the reply can't be parsed, blocks are translated one by one.
    /// - Otherwise (Google, or a non-LLM/non-Google backend such as Yandex/DeepL/Papago) the feature
    ///   transparently falls back to Google with <c>sl=auto</c>, one request per block.
    /// </summary>
    public sealed class ImageTranslationService
    {
        private const int MAX_CONCURRENT_TRANSLATIONS = 3;

        private readonly LanguageService _languageService;
        private readonly TranslationConfiguration _translationConfiguration;
        private readonly OcrGeneralConfiguration _ocrConfiguration;
        private readonly LlmProfiles _llmProfiles;
        private readonly ILogger _logger;
        private readonly AutoSourceGoogleTranslator _googleTranslator = new AutoSourceGoogleTranslator();

        public ImageTranslationService(
            LanguageService languageService,
            TranslationConfiguration translationConfiguration,
            OcrGeneralConfiguration ocrConfiguration,
            LlmProfiles llmProfiles,
            ILogger<ImageTranslationService> logger)
        {
            _languageService = languageService;
            _translationConfiguration = translationConfiguration;
            _ocrConfiguration = ocrConfiguration;
            _llmProfiles = llmProfiles;
            _logger = logger;
        }

        /// <summary>Installed Windows OCR source languages as (BCP-47 tag, display name), for the override dropdown.</summary>
        public IReadOnlyList<(string Tag, string DisplayName)> GetAvailableSourceLanguages()
        {
            return WindowsOcrPositional.GetInstalledRecognizers();
        }

        /// <summary>
        /// The target language used when the user has not overridden it in the overlay — mirrors the
        /// global "Target Language" setting rather than a hardcoded default.
        /// </summary>
        public Languages DefaultTargetLanguage => _translationConfiguration.TranslateToLang;

        /// <param name="regionImage">Encoded screenshot bytes of the selected region.</param>
        /// <param name="forcedSourceTag">BCP-47 Windows OCR recognizer tag to force, or null to auto-detect
        /// (ignored by PaddleOCR-VL, which is multilingual).</param>
        /// <param name="target">Target translation language.</param>
        public async Task<ImageTranslationResult> TranslateRegionAsync(byte[] regionImage, string forcedSourceTag, Languages target)
        {
            var (ocr, notice) = await RecognizeAsync(regionImage, forcedSourceTag).ConfigureAwait(false);
            if (ocr == null || ocr.Lines.Count == 0)
            {
                return new ImageTranslationResult
                {
                    DetectedLanguageTag = ocr?.LanguageTag,
                    ImageWidth = ocr?.ImageWidth ?? 0,
                    ImageHeight = ocr?.ImageHeight ?? 0,
                    Notice = notice
                };
            }

            var blocks = SpatialBlockGrouper.Group(ocr.Lines);
            var translations = await TranslateTextsAsync(blocks.Select(b => b.Text).ToList(), target).ConfigureAwait(false);

            var lines = blocks
                .Select(block => new TranslatedLine
                {
                    Source = block.Text,
                    Translation = translations.TryGetValue(block.Text, out var tr) ? tr : block.Text,
                    Box = block.Box,
                    IsVertical = block.IsVertical
                })
                .ToList();

            return new ImageTranslationResult
            {
                Lines = lines,
                DetectedLanguageTag = ocr.LanguageTag,
                ImageWidth = ocr.ImageWidth,
                ImageHeight = ocr.ImageHeight,
                Notice = notice
            };
        }

        private async Task<(OcrRegionResult Ocr, string Notice)> RecognizeAsync(byte[] regionImage, string forcedSourceTag)
        {
            string notice = null;
            var paddleConfiguration = _ocrConfiguration.GetConfiguration<PaddleOcrVlConfiguration>();
            if (paddleConfiguration?.Enabled == true)
            {
                try
                {
                    return (await PaddleOcrVlClient.SpotAsync(paddleConfiguration, regionImage).ConfigureAwait(false), null);
                }
                catch (PaddleOcrVlPositionsUnavailableException ex)
                {
                    _logger.LogWarning(ex, "PaddleOCR-VL returned no positions; falling back to Windows OCR");
                    notice = "PaddleOCR-VL: no text positions from server (add --special in LM Studio) — using Windows OCR";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PaddleOCR-VL spotting failed; falling back to Windows OCR");
                    notice = $"PaddleOCR-VL failed ({ex.Message}) — using Windows OCR";
                }
            }

            var windowsOcr = await WindowsOcrPositional.DetectAndRecognizeAsync(regionImage, forcedSourceTag).ConfigureAwait(false);

            return (windowsOcr, notice);
        }

        private async Task<IDictionary<string, string>> TranslateTextsAsync(IReadOnlyList<string> sources, Languages target)
        {
            var distinct = sources.Distinct().ToList();
            var targetName = _languageService.GetLanguageDescriptor(target).Language.ToString();

            var llm = CreateLlmTranslator();
            if (llm != null && distinct.Count > 1)
            {
                try
                {
                    var batch = await llm.TryTranslateBatchAutoDetectAsync(distinct, targetName).ConfigureAwait(false);
                    if (batch != null)
                    {
                        return distinct.Zip(batch).ToDictionary(pair => pair.First, pair => pair.Second);
                    }

                    _logger.LogWarning("LLM batch translation reply could not be parsed; translating blocks one by one");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LLM batch translation failed; translating blocks one by one");
                }
            }

            Func<string, Task<string>> translateOne = llm != null
                ? src => llm.TranslateAutoDetectAsync(src, targetName)
                : CreateGoogleTranslator(target);

            return await TranslateOneByOneAsync(distinct, translateOne).ConfigureAwait(false);
        }

        /// <summary>The active LLM translator when LLM translation is selected and configured, otherwise null.</summary>
        private LlmTranslator CreateLlmTranslator()
        {
            var activeLlm = _translationConfiguration.Translator == Translators.Llm ? _llmProfiles.Active : null;

            return activeLlm != null && activeLlm.Enabled
                ? new LlmTranslator(_translationConfiguration, activeLlm, _languageService, _logger)
                : null;
        }

        // Google path (sl=auto). Also the automatic fallback for Yandex/DeepL/Papago, etc.
        private Func<string, Task<string>> CreateGoogleTranslator(Languages target)
        {
            var targetIso = _languageService.GetLanguageDescriptor(target).IsoCode;
            return src => _googleTranslator.TranslateAsync(src, targetIso);
        }

        private async Task<IDictionary<string, string>> TranslateOneByOneAsync(IEnumerable<string> sources, Func<string, Task<string>> translateOne)
        {
            var map = new ConcurrentDictionary<string, string>();
            using var throttle = new SemaphoreSlim(MAX_CONCURRENT_TRANSLATIONS);

            await Task.WhenAll(sources.Select(async src =>
            {
                await throttle.WaitAsync().ConfigureAwait(false);
                try
                {
                    map[src] = await translateOne(src).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Image block translation failed; keeping source text");
                    map[src] = src;
                }
                finally
                {
                    throttle.Release();
                }
            })).ConfigureAwait(false);

            return map;
        }
    }
}
