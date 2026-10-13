using System.Collections.Generic;
using System.Text;

namespace Translumo.OCR.PaddleOcrVl
{
    /// <summary>
    /// Groups recognized lines into blocks: a sentence wrapped over several lines becomes one block,
    /// while separate items (speaker name, HUD values, button hints) stay apart.
    /// </summary>
    public static class TextBlockGrouper
    {
        // A block ending with one of these is complete, so the next line starts a new block
        private const string TERMINAL_CHARS = ".!?…:;\"”»)]}'’";
        // In CJK text a line continues only after a comma-like mark (there is no letter case to rely on)
        private const string CJK_CONTINUATION_CHARS = "，、,；";

        public static string[] Group(IReadOnlyList<string> lines, bool asian)
        {
            var blocks = new List<string>();
            StringBuilder current = null;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var trimmed = line.Trim();
                if (current != null && ContinuesBlock(current, trimmed, asian))
                {
                    current.Append(asian ? string.Empty : " ").Append(trimmed);
                    continue;
                }

                if (current != null)
                {
                    blocks.Add(current.ToString());
                }

                current = new StringBuilder(trimmed);
            }

            if (current != null)
            {
                blocks.Add(current.ToString());
            }

            return blocks.ToArray();
        }

        private static bool ContinuesBlock(StringBuilder block, string nextLine, bool asian)
        {
            var lastChar = block[block.Length - 1];
            if (asian)
            {
                return CJK_CONTINUATION_CHARS.IndexOf(lastChar) >= 0;
            }

            return TERMINAL_CHARS.IndexOf(lastChar) < 0 && char.IsLower(nextLine[0]);
        }
    }
}
