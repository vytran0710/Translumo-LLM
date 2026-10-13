using System;

namespace Translumo.OCR.PaddleOcrVl
{
    /// <summary>
    /// The server returned recognized text without PaddleOCR-VL location tokens, so text positions are unknown.
    /// LM Studio hides them unless the model is loaded with the llama.cpp "--special" argument.
    /// </summary>
    public sealed class PaddleOcrVlPositionsUnavailableException : Exception
    {
        public PaddleOcrVlPositionsUnavailableException()
            : base("The PaddleOCR-VL server returned no text positions. In LM Studio, add \"--special\" to the model's " +
                   "llama.cpp arguments; for llama-server use --special; for vLLM it works out of the box.")
        {
        }
    }
}
