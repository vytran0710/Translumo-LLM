using System;
using Translumo.OCR.PaddleOcrVl;
using Xunit;

namespace Translumo.Tests.Ocr
{
    public class TextBlockGrouperTests
    {
        [Fact]
        public void Game_screen_lines_from_lm_studio_spotting_become_four_blocks()
        {
            // Real "Spotting:" output from PaddleOCR-VL 1.6 (LM Studio) for a dialogue screen
            var lines = "Aria\nHP 120/150\nWe must reach the northern gate before\nthe sun sets, or the whole village is lost.\n[E] Continue [Q] Skip"
                .Split('\n');

            var blocks = TextBlockGrouper.Group(lines, asian: false);

            Assert.Equal(new[]
            {
                "Aria",
                "HP 120/150",
                "We must reach the northern gate before the sun sets, or the whole village is lost.",
                "[E] Continue [Q] Skip"
            }, blocks);
        }

        [Fact]
        public void Speaker_name_followed_by_capitalized_line_stays_separate()
        {
            var blocks = TextBlockGrouper.Group(new[] { "Aria", "We must go." }, asian: false);

            Assert.Equal(new[] { "Aria", "We must go." }, blocks);
        }

        [Theory]
        [InlineData("The dragon awaits.")]
        [InlineData("Where is it?")]
        [InlineData("Run!")]
        [InlineData("He said:")]
        [InlineData("\"Hello\"")]
        public void Sentence_ending_punctuation_splits_even_before_lowercase(string first)
        {
            var blocks = TextBlockGrouper.Group(new[] { first, "then something else" }, asian: false);

            Assert.Equal(2, blocks.Length);
        }

        [Fact]
        public void Multiple_wrapped_lines_join_into_one_block()
        {
            var blocks = TextBlockGrouper.Group(new[] { "one line that", "keeps going and", "ends here." }, asian: false);

            Assert.Equal(new[] { "one line that keeps going and ends here." }, blocks);
        }

        [Fact]
        public void Cyrillic_lowercase_continuation_joins()
        {
            var blocks = TextBlockGrouper.Group(new[] { "Мы должны дойти до", "северных ворот." }, asian: false);

            Assert.Equal(new[] { "Мы должны дойти до северных ворот." }, blocks);
        }

        [Fact]
        public void Cjk_joins_without_space_only_after_continuation_mark()
        {
            var blocks = TextBlockGrouper.Group(new[] { "艾莉娅", "我们必须在日落前赶到北门，", "否则整个村庄就完了。", "继续" }, asian: true);

            Assert.Equal(new[] { "艾莉娅", "我们必须在日落前赶到北门，否则整个村庄就完了。", "继续" }, blocks);
        }

        [Fact]
        public void Blank_lines_are_skipped_and_lines_trimmed()
        {
            var blocks = TextBlockGrouper.Group(new[] { "  first part  ", "", "   ", "second part." }, asian: false);

            Assert.Equal(new[] { "first part second part." }, blocks);
        }

        [Fact]
        public void Empty_input_returns_empty_and_single_line_is_unchanged()
        {
            Assert.Empty(TextBlockGrouper.Group(Array.Empty<string>(), asian: false));
            Assert.Equal(new[] { "Only line" }, TextBlockGrouper.Group(new[] { "Only line" }, asian: false));
        }
    }
}
