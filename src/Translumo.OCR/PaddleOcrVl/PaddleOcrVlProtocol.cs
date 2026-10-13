using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OpenCvSharp;
using Translumo.OCR.WindowsOCR;

namespace Translumo.OCR.PaddleOcrVl
{
    /// <summary>
    /// OpenAI-compatible chat/completions wire format for PaddleOCR-VL. Kept free of I/O so it can be unit tested.
    /// </summary>
    internal static class PaddleOcrVlProtocol
    {
        /// <summary>PaddleOCR-VL task prompt for plain text recognition.</summary>
        public const string OCR_PROMPT = "OCR:";
        /// <summary>PaddleOCR-VL 1.5+ task prompt that returns one line per detected text region.</summary>
        public const string SPOTTING_PROMPT = "Spotting:";
        public const int MAX_TOKENS = 1024;
        // Spotting appends 8 location tokens per line, so it needs a larger budget
        public const int SPOTTING_MAX_TOKENS = 2048;
        // Location tokens encode coordinates normalized to 0..LOC_SCALE
        public const float LOC_SCALE = 1000f;
        private const int LOC_TOKENS_PER_LINE = 8;

        private const string CHAT_COMPLETIONS_SUFFIX = "/chat/completions";

        // Special tokens a server may pass through (e.g. LM Studio/llama.cpp with --special):
        // <|LOC_123|>, <|LOC_BEGIN|>, ... and the end-of-sequence marker </s>
        private static readonly Regex SpecialTokenRegex = new Regex(@"<\|[^|>]+\|>|</s>", RegexOptions.Compiled);
        private static readonly Regex LocTokenRegex = new Regex(@"<\|LOC_(\d+)\|>", RegexOptions.Compiled);

        public static string GetPrompt(PaddleOcrVlConfiguration configuration) =>
            configuration.DetectLines ? SPOTTING_PROMPT : OCR_PROMPT;

        /// <summary>Trims whitespace and trailing slashes, and tolerates a pasted full ".../chat/completions" URL.</summary>
        public static string NormalizeBaseUrl(string endpoint)
        {
            var url = (endpoint ?? string.Empty).Trim().TrimEnd('/');
            if (url.EndsWith(CHAT_COMPLETIONS_SUFFIX, StringComparison.OrdinalIgnoreCase))
            {
                url = url[..^CHAT_COMPLETIONS_SUFFIX.Length];
            }

            return url;
        }

        public static string BuildRequestJson(string modelName, byte[] pngImage, string prompt = OCR_PROMPT,
            int maxTokens = MAX_TOKENS, bool keepSpecialTokens = false)
        {
            var dataUri = "data:image/png;base64," + Convert.ToBase64String(pngImage);
            var request = new JsonObject
            {
                ["model"] = modelName,
                ["temperature"] = 0,
                ["max_tokens"] = maxTokens,
                ["messages"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "image_url",
                                ["image_url"] = new JsonObject { ["url"] = dataUri }
                            },
                            new JsonObject
                            {
                                ["type"] = "text",
                                ["text"] = prompt
                            }
                        }
                    }
                }
            };
            if (keepSpecialTokens)
            {
                // vLLM honors this; other servers ignore unknown fields (LM Studio/llama.cpp need --special instead)
                request["skip_special_tokens"] = false;
            }

            return request.ToJsonString();
        }

        public static HttpRequestMessage BuildChatRequest(PaddleOcrVlConfiguration configuration, byte[] pngImage)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, NormalizeBaseUrl(configuration.Endpoint) + CHAT_COMPLETIONS_SUFFIX)
            {
                Content = new StringContent(BuildRequestJson(configuration.ModelName, pngImage, GetPrompt(configuration)),
                    Encoding.UTF8, "application/json")
            };
            ApplyAuthorization(request, configuration.ApiKey);

            return request;
        }

        /// <summary>"Spotting:" request that asks the server to keep the location tokens (text positions).</summary>
        public static HttpRequestMessage BuildSpottingRequest(PaddleOcrVlConfiguration configuration, byte[] pngImage)
        {
            var json = BuildRequestJson(configuration.ModelName, pngImage, SPOTTING_PROMPT, SPOTTING_MAX_TOKENS, keepSpecialTokens: true);
            var request = new HttpRequestMessage(HttpMethod.Post, NormalizeBaseUrl(configuration.Endpoint) + CHAT_COMPLETIONS_SUFFIX)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            ApplyAuthorization(request, configuration.ApiKey);

            return request;
        }

        public static HttpRequestMessage BuildModelsRequest(PaddleOcrVlConfiguration configuration)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, NormalizeBaseUrl(configuration.Endpoint) + "/models");
            ApplyAuthorization(request, configuration.ApiKey);

            return request;
        }

        /// <summary>Extracts recognized lines from a chat/completions response; empty when there is no content.</summary>
        public static string[] ParseLines(string responseJson)
        {
            var content = ReadContent(responseJson);
            if (content == null)
            {
                return Array.Empty<string>();
            }

            return SpecialTokenRegex.Replace(content, string.Empty)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();
        }

        /// <summary>Returns choices[0].message.content, or null when the response has no text content.</summary>
        public static string ReadContent(string responseJson)
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return null;
            }

            if (!choices[0].TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return content.GetString();
        }

        /// <summary>
        /// Parses "Spotting:" output: each line is its text followed by 8 location tokens
        /// (x,y of top-left, top-right, bottom-right, bottom-left, normalized 0..1000).
        /// Returns pixel bounding boxes for <paramref name="imageWidth"/> x <paramref name="imageHeight"/>;
        /// lines without a full set of coordinates are skipped.
        /// </summary>
        public static List<PositionalOcrLine> ParseSpotting(string content, int imageWidth, int imageHeight)
        {
            var result = new List<PositionalOcrLine>();
            if (string.IsNullOrEmpty(content))
            {
                return result;
            }

            foreach (var rawLine in content.Split('\n'))
            {
                var locs = LocTokenRegex.Matches(rawLine);
                if (locs.Count < LOC_TOKENS_PER_LINE)
                {
                    continue;
                }

                var text = SpecialTokenRegex.Replace(rawLine, string.Empty).Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                var coords = locs.Take(LOC_TOKENS_PER_LINE)
                    .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / LOC_SCALE)
                    .ToArray();
                var xs = new[] { coords[0], coords[2], coords[4], coords[6] };
                var ys = new[] { coords[1], coords[3], coords[5], coords[7] };
                var left = xs.Min() * imageWidth;
                var top = ys.Min() * imageHeight;

                result.Add(new PositionalOcrLine
                {
                    Text = text,
                    Box = new RectangleF(left, top, xs.Max() * imageWidth - left, ys.Max() * imageHeight - top)
                });
            }

            return result;
        }

        /// <summary>True when the content carries at least one location token.</summary>
        public static bool HasLocationTokens(string content) =>
            !string.IsNullOrEmpty(content) && LocTokenRegex.IsMatch(content);

        /// <summary>Returns the model ids listed by a GET /models response.</summary>
        public static IReadOnlyList<string> ParseModelIds(string responseJson)
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return data.EnumerateArray()
                .Where(model => model.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                .Select(model => model.GetProperty("id").GetString()!)
                .ToArray();
        }

        /// <summary>Screens are captured as TIFF, which vision servers don't accept; re-encode as PNG.</summary>
        public static byte[] ToPng(byte[] image)
        {
            using var mat = Cv2.ImDecode(image, ImreadModes.Color);
            if (mat.Empty())
            {
                throw new ArgumentException("Captured image could not be decoded", nameof(image));
            }

            Cv2.ImEncode(".png", mat, out var png);

            return png;
        }

        private static void ApplyAuthorization(HttpRequestMessage request, string apiKey)
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            }
        }
    }
}
