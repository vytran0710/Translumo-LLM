using Translumo.Infrastructure.Language;

namespace Translumo.OCR
{
    public interface IOCREngine
    {
        byte PrimaryPriority { get; }
        bool SecondaryPrimaryCheck { get; }
        int Confidence { get; }
        Languages DetectionLanguage { get; }
        /// <summary>When true, detected lines are kept on separate lines even if "Keep source formatting" is off.</summary>
        bool PreservesLineBreaks => false;

        string[] GetTextLines(byte[] image);
    }
}
