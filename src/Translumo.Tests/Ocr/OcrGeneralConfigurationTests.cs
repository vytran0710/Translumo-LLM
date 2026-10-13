using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Translumo.OCR.Configuration;
using Translumo.OCR.EasyOCR;
using Translumo.OCR.PaddleOcrVl;
using Translumo.OCR.Tesseract;
using Translumo.OCR.WindowsOCR;
using Xunit;

namespace Translumo.Tests.Ocr
{
    public class OcrGeneralConfigurationTests
    {
        [Fact]
        public void Default_includes_disabled_paddle_ocr_vl()
        {
            var configuration = OcrGeneralConfiguration.Default;

            var paddle = configuration.GetConfiguration<PaddleOcrVlConfiguration>();
            Assert.NotNull(paddle);
            Assert.False(paddle.Enabled);
        }

        [Fact]
        public void Legacy_saved_array_gets_missing_engines_appended_and_keeps_saved_flags()
        {
            var configuration = OcrGeneralConfiguration.Default;
            var legacy = new OcrConfiguration[]
            {
                new EasyOCRConfiguration(), new WindowsOCRConfiguration { Enabled = true }, new TesseractOCRConfiguration()
            };

            configuration.OcrConfigurations = legacy;

            Assert.Equal(4, configuration.OcrConfigurations.Length);
            Assert.True(configuration.GetConfiguration<WindowsOCRConfiguration>().Enabled);
            Assert.NotNull(configuration.GetConfiguration<PaddleOcrVlConfiguration>());
            Assert.Equal(legacy, configuration.OcrConfigurations.Take(3));
        }

        [Fact]
        public void Paddle_settings_survive_xml_round_trip()
        {
            var configuration = OcrGeneralConfiguration.Default;
            var paddle = configuration.GetConfiguration<PaddleOcrVlConfiguration>();
            paddle.Enabled = true;
            paddle.Endpoint = "http://gpu-box:9000/v1";
            paddle.ModelName = "PaddleOCR-VL-0.9B";
            paddle.ApiKey = "k";
            paddle.DetectLines = false;
            var serializer = new XmlSerializer(typeof(OcrGeneralConfiguration));
            using var writer = new StringWriter();

            serializer.Serialize(writer, configuration);
            using var reader = new StringReader(writer.ToString());
            var restored = (OcrGeneralConfiguration)serializer.Deserialize(reader)!;

            var restoredPaddle = restored.GetConfiguration<PaddleOcrVlConfiguration>();
            Assert.True(restoredPaddle.Enabled);
            Assert.Equal("http://gpu-box:9000/v1", restoredPaddle.Endpoint);
            Assert.Equal("PaddleOCR-VL-0.9B", restoredPaddle.ModelName);
            Assert.Equal("k", restoredPaddle.ApiKey);
            Assert.False(restoredPaddle.DetectLines);
            Assert.Equal(4, restored.OcrConfigurations.Length);
        }

        [Fact]
        public void Changes_to_paddle_settings_bubble_up_as_ocr_configurations_change()
        {
            var configuration = OcrGeneralConfiguration.Default;
            string? changed = null;
            configuration.PropertyChanged += (_, e) => changed = e.PropertyName;

            configuration.GetConfiguration<PaddleOcrVlConfiguration>().Endpoint = "http://other/v1";

            Assert.Equal(nameof(OcrGeneralConfiguration.OcrConfigurations), changed);
        }
    }
}
