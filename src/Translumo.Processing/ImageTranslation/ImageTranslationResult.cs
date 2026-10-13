using System.Collections.Generic;
using System.Drawing;

namespace Translumo.Processing.ImageTranslation
{
    /// <summary>One recognized text block with its translation and pixel bounding box (image-relative).</summary>
    public sealed class TranslatedLine
    {
        public string Source { get; init; }

        public string Translation { get; init; }

        public RectangleF Box { get; init; }

        /// <summary>Source text runs top-to-bottom (e.g. manga columns); the overlay widens its label.</summary>
        public bool IsVertical { get; init; }
    }

    /// <summary>Full result of translating a captured region (Google Lens style).</summary>
    public sealed class ImageTranslationResult
    {
        public IReadOnlyList<TranslatedLine> Lines { get; init; } = new List<TranslatedLine>();

        /// <summary>BCP-47 tag of the recognizer used (e.g. "en-US"); null when nothing was detected.</summary>
        public string DetectedLanguageTag { get; init; }

        public int ImageWidth { get; init; }

        public int ImageHeight { get; init; }

        /// <summary>Optional message for the user, e.g. why a fallback recognizer was used.</summary>
        public string Notice { get; init; }

        public bool HasText => Lines.Count > 0;
    }
}
