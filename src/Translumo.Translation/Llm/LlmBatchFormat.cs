using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Translumo.Translation.Llm
{
    /// <summary>
    /// Wire format for translating many short texts in one LLM request: the user message is a JSON array
    /// of source strings and the model must answer with a JSON array of translations in the same order.
    /// </summary>
    public static class LlmBatchFormat
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            // Keep CJK etc. readable for the model instead of \uXXXX escapes
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string BuildSystemPrompt(string targetLanguageName) =>
$@"You are a professional translator localizing text captured from a screen (manga, comics, game UI, subtitles, signs, etc.).
The user message is a JSON array of text blocks taken from the same image, in reading order. Use the other blocks as context.
The source language is unknown and may differ per block; detect it automatically.
Translate every block into {targetLanguageName}, preserving meaning, tone and character voice.
Reply with ONLY a JSON array of strings: exactly one translation per input block, in the same order. No commentary, no code fences.
If a block is already in {targetLanguageName}, or is a sound effect or garbled text that cannot be translated, return it unchanged.";

        public static string BuildUserMessage(IReadOnlyList<string> sourceTexts) =>
            JsonSerializer.Serialize(sourceTexts, SerializerOptions);

        /// <summary>
        /// Parses the model's reply. Tolerates code fences and text around the array. Returns null unless the reply
        /// is a JSON array of exactly <paramref name="expectedCount"/> strings.
        /// </summary>
        public static IReadOnlyList<string> ParseReply(string reply, int expectedCount)
        {
            if (string.IsNullOrWhiteSpace(reply))
            {
                return null;
            }

            var start = reply.IndexOf('[');
            var end = reply.LastIndexOf(']');
            if (start < 0 || end <= start)
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(reply.Substring(start, end - start + 1));
                if (doc.RootElement.ValueKind != JsonValueKind.Array
                    || doc.RootElement.GetArrayLength() != expectedCount
                    || doc.RootElement.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
                {
                    return null;
                }

                return doc.RootElement.EnumerateArray().Select(item => item.GetString()).ToArray();
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
