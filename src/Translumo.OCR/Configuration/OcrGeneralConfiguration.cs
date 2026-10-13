using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Translumo.Infrastructure.Language;
using Translumo.OCR.EasyOCR;
using Translumo.OCR.PaddleOcrVl;
using Translumo.OCR.Tesseract;
using Translumo.OCR.WindowsOCR;
using Translumo.Utils.Extensions;

namespace Translumo.OCR.Configuration
{
    public class OcrGeneralConfiguration : INotifyPropertyChanged
    {
        public static OcrGeneralConfiguration Default => new OcrGeneralConfiguration()
        {
            OcrConfigurations = CreateDefaultConfigurations(),
        };

        public OcrConfiguration[] OcrConfigurations
        {
            get => _ocrConfigurations;
            set
            {
                // Settings saved by older versions lack newly added engines; MapTo replaces the whole array on load
                value = WithMissingDefaults(value);
                if (value != _ocrConfigurations)
                {
                    _ocrConfigurations?.ForEach(conf => conf.PropertyChanged -= OcrConfigurationOnPropertyChanged);
                    value?.ForEach(conf => conf.PropertyChanged += OcrConfigurationOnPropertyChanged);
                }

                _ocrConfigurations = value;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private OcrConfiguration[] _ocrConfigurations;

        public TConfiguration GetConfiguration<TConfiguration>()
            where TConfiguration : OcrConfiguration
        {
            return (TConfiguration) OcrConfigurations.FirstOrDefault(conf => conf.GetType() == typeof(TConfiguration));
        }

        private static OcrConfiguration[] CreateDefaultConfigurations()
        {
            return new OcrConfiguration[]
            {
                new EasyOCRConfiguration(), new WindowsOCRConfiguration(), new TesseractOCRConfiguration(),
                new PaddleOcrVlConfiguration()
            };
        }

        private static OcrConfiguration[] WithMissingDefaults(OcrConfiguration[] configurations)
        {
            if (configurations == null)
            {
                return null;
            }

            var missing = CreateDefaultConfigurations()
                .Where(defaultConf => configurations.All(conf => conf.GetType() != defaultConf.GetType()))
                .ToArray();

            return missing.Length == 0 ? configurations : configurations.Concat(missing).ToArray();
        }

        private void OcrConfigurationOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(OcrConfigurations));
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
