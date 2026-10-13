using Translumo.OCR.Configuration;

namespace Translumo.OCR.PaddleOcrVl
{
    /// <summary>
    /// Settings for the PaddleOCR-VL engine, which is served by an OpenAI-compatible endpoint
    /// (vLLM / SGLang / PaddleOCR genai_server, local or remote).
    /// </summary>
    public class PaddleOcrVlConfiguration : OcrConfiguration
    {
        public const string DEFAULT_ENDPOINT = "http://localhost:8000/v1";
        public const string DEFAULT_MODEL_NAME = "PaddlePaddle/PaddleOCR-VL";
        public const int DEFAULT_TIMEOUT_SECONDS = 15;

        /// <summary>Base URL of the OpenAI-compatible API, e.g. http://localhost:8000/v1</summary>
        public string Endpoint
        {
            get => _endpoint;
            set => SetValue(ref _endpoint, value);
        }

        public string ModelName
        {
            get => _modelName;
            set => SetValue(ref _modelName, value);
        }

        /// <summary>Optional; sent as a Bearer token only when not empty.</summary>
        public string ApiKey
        {
            get => _apiKey;
            set => SetValue(ref _apiKey, value);
        }

        public int TimeoutSeconds
        {
            get => _timeoutSeconds;
            set => SetValue(ref _timeoutSeconds, value);
        }

        /// <summary>
        /// Uses the "Spotting:" task (PaddleOCR-VL 1.5+), which returns every visual line separately,
        /// instead of "OCR:", which may merge or drop lines.
        /// </summary>
        public bool DetectLines
        {
            get => _detectLines;
            set => SetValue(ref _detectLines, value);
        }

        private string _endpoint = DEFAULT_ENDPOINT;
        private bool _detectLines = true;
        private string _modelName = DEFAULT_MODEL_NAME;
        private string _apiKey = string.Empty;
        private int _timeoutSeconds = DEFAULT_TIMEOUT_SECONDS;

        private void SetValue<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            OnPropertyChanged(propertyName);
        }
    }
}
