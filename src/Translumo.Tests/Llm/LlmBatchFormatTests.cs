using Translumo.Translation.Llm;
using Xunit;

namespace Translumo.Tests.Llm
{
    public class LlmBatchFormatTests
    {
        [Fact]
        public void User_message_is_a_json_array_with_readable_cjk()
        {
            var message = LlmBatchFormat.BuildUserMessage(new[] { "魚拾った～", "Say \"hi\"" });

            Assert.Equal("[\"魚拾った～\",\"Say \\\"hi\\\"\"]", message);
        }

        [Fact]
        public void System_prompt_names_target_language_and_demands_json_array()
        {
            var prompt = LlmBatchFormat.BuildSystemPrompt("English");

            Assert.Contains("English", prompt);
            Assert.Contains("JSON array", prompt);
        }

        [Theory]
        [InlineData("[\"I found a fish~\",\"Can't we eat it?\"]")]
        [InlineData("```json\n[\"I found a fish~\",\"Can't we eat it?\"]\n```")]
        [InlineData("Here you go: [\"I found a fish~\", \"Can't we eat it?\"] Hope it helps")]
        public void ParseReply_accepts_array_with_fences_or_surrounding_text(string reply)
        {
            Assert.Equal(new[] { "I found a fish~", "Can't we eat it?" }, LlmBatchFormat.ParseReply(reply, 2));
        }

        [Theory]
        [InlineData("[\"only one\"]")]                 // wrong count
        [InlineData("[\"a\", 2]")]                     // non-string item
        [InlineData("I found a fish~")]                // not an array
        [InlineData("[\"unterminated, \"x\"")]         // malformed
        [InlineData("")]
        public void ParseReply_returns_null_when_unusable(string reply)
        {
            Assert.Null(LlmBatchFormat.ParseReply(reply, 2));
        }
    }
}
