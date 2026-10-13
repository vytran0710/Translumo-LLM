using System.Linq;
using Translumo.OCR.PaddleOcrVl;
using Xunit;

namespace Translumo.Tests.Ocr
{
    public class PaddleOcrVlSpottingTests
    {
        // Example from the PaddleOCR-VL-1.5 paper: text + x,y of TL, TR, BR, BL normalized to 0..1000
        private const string DREAM = "DREAM<|LOC_253|><|LOC_286|><|LOC_346|><|LOC_298|><|LOC_345|><|LOC_339|><|LOC_252|><|LOC_330|>";

        [Fact]
        public void ParseSpotting_maps_quadrilateral_to_pixel_bounding_box()
        {
            var lines = PaddleOcrVlProtocol.ParseSpotting(DREAM, 2000, 1000);

            var line = Assert.Single(lines);
            Assert.Equal("DREAM", line.Text);
            Assert.Equal(504f, line.Box.X, 2);      // min x 252 → 252/1000*2000
            Assert.Equal(286f, line.Box.Y, 2);      // min y 286
            Assert.Equal(188f, line.Box.Width, 2);  // (346-252)/1000*2000
            Assert.Equal(53f, line.Box.Height, 2);  // (339-286)/1000*1000
        }

        [Fact]
        public void ParseSpotting_reads_lm_studio_output_and_skips_incomplete_lines()
        {
            // Shape of real LM Studio output with --special (ends with </s>)
            const string content =
                "あざらしっぽいよね<|LOC_604|><|LOC_43|><|LOC_880|><|LOC_43|><|LOC_880|><|LOC_67|><|LOC_604|><|LOC_67|>\n" +
                "北上様な？<|LOC_148|><|LOC_97|><|LOC_184|><|LOC_97|><|LOC_184|><|LOC_217|><|LOC_148|><|LOC_217|>\n" +
                "broken<|LOC_1|><|LOC_2|><|LOC_3|>\n" +
                "no coordinates here\n" +
                "<|LOC_1|><|LOC_2|><|LOC_3|><|LOC_4|><|LOC_5|><|LOC_6|><|LOC_7|><|LOC_8|></s>";

            var lines = PaddleOcrVlProtocol.ParseSpotting(content, 1000, 1000);

            Assert.Equal(new[] { "あざらしっぽいよね", "北上様な？" }, lines.Select(l => l.Text));
            Assert.False(lines[0].IsVertical);
            Assert.True(lines[1].IsVertical);
        }

        [Fact]
        public void ParseSpotting_strips_other_special_tokens_from_text()
        {
            var lines = PaddleOcrVlProtocol.ParseSpotting("<|LOC_BEGIN|>" + DREAM + "<|LOC_END|></s>", 1000, 1000);

            Assert.Equal("DREAM", Assert.Single(lines).Text);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("plain text\nwithout positions")]
        public void ParseSpotting_without_location_tokens_returns_nothing(string content)
        {
            Assert.Empty(PaddleOcrVlProtocol.ParseSpotting(content, 1000, 1000));
            Assert.False(PaddleOcrVlProtocol.HasLocationTokens(content));
        }

        [Fact]
        public void ParseLines_strips_end_of_sequence_marker()
        {
            // With --special, every reply (also "OCR:") ends with </s>
            const string json = "{\"choices\":[{\"message\":{\"content\":\"まだ子どもじゃ\\nないかな？</s>\"}}]}";

            Assert.Equal(new[] { "まだ子どもじゃ", "ないかな？" }, PaddleOcrVlProtocol.ParseLines(json));
        }
    }
}
