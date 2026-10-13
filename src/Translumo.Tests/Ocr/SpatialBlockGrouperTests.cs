using System.Drawing;
using System.Linq;
using Translumo.OCR.PaddleOcrVl;
using Translumo.OCR.WindowsOCR;
using Xunit;

namespace Translumo.Tests.Ocr
{
    public class SpatialBlockGrouperTests
    {
        private static PositionalOcrLine Line(string text, float x, float y, float w, float h) =>
            new PositionalOcrLine { Text = text, Box = new RectangleF(x, y, w, h) };

        [Fact]
        public void Vertical_columns_of_one_bubble_join_right_to_left()
        {
            // Spotting lists the columns left-to-right; Japanese reads them right-to-left
            var lines = new[]
            {
                Line("ねーだろ", 100, 100, 30, 120),
                Line("それ魚じゃ", 140, 100, 30, 150),
                Line("ていうか", 180, 100, 30, 120)
            };

            var block = Assert.Single(SpatialBlockGrouper.Group(lines));

            Assert.Equal("ていうかそれ魚じゃねーだろ", block.Text);
            Assert.Equal(new RectangleF(100, 100, 110, 150), block.Box);
        }

        [Fact]
        public void Separate_bubbles_stay_separate_in_model_order()
        {
            var lines = new[]
            {
                Line("あねごー", 700, 140, 30, 120),
                Line("北上様な？", 130, 120, 30, 150)
            };

            var blocks = SpatialBlockGrouper.Group(lines);

            Assert.Equal(new[] { "あねごー", "北上様な？" }, blocks.Select(b => b.Text));
        }

        [Fact]
        public void Short_square_column_still_joins_its_bubble()
        {
            // A 1-2 character column is roughly square, so its orientation is ambiguous
            var lines = new[]
            {
                Line("な？", 100, 100, 30, 40),
                Line("北上様", 140, 100, 30, 100)
            };

            Assert.Equal("北上様な？", Assert.Single(SpatialBlockGrouper.Group(lines)).Text);
        }

        [Fact]
        public void Horizontal_lines_keep_menu_items_apart_and_rejoin_wrapped_sentences()
        {
            var lines = new[]
            {
                Line("northern gate.", 50, 220, 180, 30),
                Line("Start Game", 50, 100, 200, 30),
                Line("Options", 50, 140, 120, 30),
                Line("We must reach the", 50, 180, 220, 30)
            };

            var block = Assert.Single(SpatialBlockGrouper.Group(lines));

            Assert.Equal("Start Game\nOptions\nWe must reach the northern gate.", block.Text);
        }

        [Fact]
        public void Lines_a_full_line_height_apart_form_separate_blocks()
        {
            var lines = new[]
            {
                Line("Options", 50, 140, 120, 30),
                Line("We must reach the gate.", 50, 200, 220, 30)
            };

            Assert.Equal(2, SpatialBlockGrouper.Group(lines).Count);
        }

        [Fact]
        public void Vertical_and_horizontal_lines_do_not_merge()
        {
            var lines = new[]
            {
                Line("縦書きの台詞", 100, 100, 30, 180),
                Line("HP 120/150", 135, 100, 200, 30)
            };

            Assert.Equal(2, SpatialBlockGrouper.Group(lines).Count);
        }

        [Fact]
        public void Empty_input_returns_no_blocks()
        {
            Assert.Empty(SpatialBlockGrouper.Group(new PositionalOcrLine[0]));
        }
    }
}
