using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OpenCvSharp;

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

        private const string CHAT_COMPLETIONS_SUFFIX = "/chat/completions";

        // Location tokens emitted by "Spotting:" (e.g. <|LOC_123|>); some servers don't hide them
        private static readonly Regex SpecialTokenRegex = new Regex(@"<\|[A-Za-z_]+\d*\|>", RegexOptions.Compiled);

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

        public static string BuildRequestJson(string modelName, byte[] pngImage, string prompt = OCR_PROMPT)
        {
            var dataUri = "data:image/png;base64," + Convert.ToBase64String(pngImage);
            var request = new JsonObject
            {
                ["model"] = modelName,
                ["temperature"] = 0,
                ["max_tokens"] = MAX_TOKENS,
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

        public static HttpRequestMessage BuildModelsRequest(PaddleOcrVlConfiguration configuration)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, NormalizeBaseUrl(configuration.Endpoint) + "/models");
            ApplyAuthorization(request, configuration.ApiKey);

            return request;
        }

        /// <summary>Extracts recognized lines from a chat/completions response; empty when there is no content.</summary>
        public static string[] ParseLines(string responseJson)
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return Array.Empty<string>();
            }

            if (!choices[0].TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.String)
            {
                return Array.Empty<string>();
            }

            return SpecialTokenRegex.Replace(content.GetString()!, string.Empty)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();
        }

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
