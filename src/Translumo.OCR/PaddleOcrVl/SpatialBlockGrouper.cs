using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Translumo.OCR.WindowsOCR;

namespace Translumo.OCR.PaddleOcrVl
{
    /// <summary>
    /// Groups positioned text lines into blocks (e.g. one per speech bubble) so each block is translated as a whole.
    /// Lines merge when their boxes, padded by half a character, touch and their orientations are compatible.
    /// Vertical blocks read their columns right-to-left (Japanese/Chinese); horizontal blocks read top-to-bottom.
    /// </summary>
    public static class SpatialBlockGrouper
    {
        // Lines whose aspect ratio is within this range (e.g. a 1-2 character column) can join either orientation
        private const float NEUTRAL_ASPECT = 1.5f;

        private enum Orientation { Horizontal, Vertical, Neutral }

        public static List<PositionalOcrLine> Group(IReadOnlyList<PositionalOcrLine> lines)
        {
            var parents = Enumerable.Range(0, lines.Count).ToArray();
            for (var i = 0; i < lines.Count; i++)
            {
                for (var j = i + 1; j < lines.Count; j++)
                {
                    if (ShouldMerge(lines[i], lines[j]))
                    {
                        parents[Find(parents, i)] = Find(parents, j);
                    }
                }
            }

            return Enumerable.Range(0, lines.Count)
                .GroupBy(i => Find(parents, i))
                .OrderBy(group => group.Min()) // keep the model's reading order between blocks
                .Select(group => BuildBlock(group.Select(i => lines[i]).ToList()))
                .ToList();
        }

        private static bool ShouldMerge(PositionalOcrLine a, PositionalOcrLine b)
        {
            var orientationA = GetOrientation(a.Box);
            var orientationB = GetOrientation(b.Box);
            if (orientationA != orientationB && orientationA != Orientation.Neutral && orientationB != Orientation.Neutral)
            {
                return false;
            }

            return Pad(a.Box).IntersectsWith(Pad(b.Box));
        }

        private static PositionalOcrLine BuildBlock(List<PositionalOcrLine> members)
        {
            var box = members.Select(m => m.Box).Aggregate(RectangleF.Union);
            var vertical = members.Any(m => GetOrientation(m.Box) == Orientation.Vertical);
            if (vertical)
            {
                // Vertical columns are one wrapped text: read right-to-left and join seamlessly
                var columns = members.OrderByDescending(m => m.Box.X + m.Box.Width / 2).Select(m => m.Text);
                return new PositionalOcrLine { Text = string.Concat(columns), Box = box };
            }

            // Horizontal lines top-to-bottom: rejoin wrapped sentences, keep separate items (menus, names) on their own lines
            var lines = members.OrderBy(m => m.Box.Y).ThenBy(m => m.Box.X).Select(m => m.Text).ToList();
            var asian = lines.All(line => line.Length > 0 && IsCjk(line[0]));
            var text = string.Join("\n", TextBlockGrouper.Group(lines, asian));

            return new PositionalOcrLine { Text = text, Box = box };
        }

        private static Orientation GetOrientation(RectangleF box)
        {
            if (box.Height > box.Width * NEUTRAL_ASPECT)
            {
                return Orientation.Vertical;
            }

            return box.Width > box.Height * NEUTRAL_ASPECT ? Orientation.Horizontal : Orientation.Neutral;
        }

        // Half the line thickness (≈ half a character) on every side
        private static RectangleF Pad(RectangleF box)
        {
            var margin = Math.Min(box.Width, box.Height) / 2;
            return RectangleF.Inflate(box, margin, margin);
        }

        private static int Find(int[] parents, int i)
        {
            while (parents[i] != i)
            {
                parents[i] = parents[parents[i]];
                i = parents[i];
            }

            return i;
        }

        private static bool IsCjk(char c) =>
            (c >= 0x3000 && c <= 0x30FF) || // CJK punctuation, hiragana, katakana
            (c >= 0x3400 && c <= 0x9FFF) || // CJK ideographs
            (c >= 0xAC00 && c <= 0xD7A3) || // Hangul
            (c >= 0xFF00 && c <= 0xFFEF);   // full-width forms (！？～)
    }
}
