using NUnit.Framework;
using ShaoLu.Services;
using ShaoLu.Services.Ocr;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace NUnitTest
{
    /// <summary>
    /// OCR 扩展加载与禁用行为：
    /// OCR 引擎单独发布（extensions\Ocr），未安装时 OCR 相关功能必须变成“不可用”而不是崩溃；
    /// 扩展存在时必须能真正识别文字（含 Tesseract 原生库的定位逻辑）。
    /// </summary>
    [TestFixture]
    [Apartment(System.Threading.ApartmentState.STA)]
    public class OcrExtensionTests
    {
        [TearDown]
        public void TearDown()
        {
            OcrExtensionHost.ResetForTests();
        }

        /// <summary>扩展构建输出目录（Debug 优先，其次 Release）；仓库里没有构建过时返回 null</summary>
        private static string FindBuiltExtensionDirectory()
        {
            string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            var candidates = new[]
            {
                Path.Combine(repoRoot, "OcrExtension", "bin", "x64", "Debug"),
                Path.Combine(repoRoot, "OcrExtension", "bin", "x64", "Release"),
            };

            return candidates.FirstOrDefault(dir =>
                Directory.Exists(dir) && File.Exists(Path.Combine(dir, "ShaoLu.OcrExtension.dll")));
        }

        /// <summary>生成一张带文字的 PNG，返回字节</summary>
        private static byte[] CreateTextPng(string text)
        {
            using (var bmp = new Bitmap(460, 100))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    using (var font = new Font("Consolas", 34, FontStyle.Bold))
                        g.DrawString(text, font, Brushes.Black, 8, 22);
                }
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }

        [Test]
        public void Host_NoExtensionInstalled_IsUnavailableWithReason()
        {
            // 测试进程的程序目录下没有 extensions\Ocr：按“未安装”处理
            OcrExtensionHost.ResetForTests();
            if (Directory.Exists(OcrExtensionHost.ExtensionDirectory))
                Assert.Ignore($"测试目录下已存在扩展：{OcrExtensionHost.ExtensionDirectory}");

            Assert.That(OcrExtensionHost.IsAvailable, Is.False);
            Assert.That(OcrExtensionHost.UnavailableReason, Is.Not.Null.And.Not.Empty, "不可用时必须给出原因（界面提示用）");
            Assert.That(OCRService.IsAvailable, Is.False);
            Assert.That(OCRService.EngineDescription, Is.Empty);
        }

        [Test]
        public void OcrService_NoExtension_RecognizeReturnsEmptyWithoutThrowing()
        {
            OcrExtensionHost.ResetForTests();
            if (Directory.Exists(OcrExtensionHost.ExtensionDirectory))
                Assert.Ignore("测试目录下已存在扩展，无法验证“未安装”分支");

            string text = null;
            using (var bmp = new Bitmap(20, 20))
            {
                Assert.DoesNotThrow((TestDelegate)(() => text = OCRService.Recognize(bmp)));
            }
            Assert.That(text, Is.Empty);
        }

        [Test]
        public void GetInputStep_NoExtension_DoesNotOfferOcrModeAndReportsHint()
        {
            OcrExtensionHost.ResetForTests();
            if (Directory.Exists(OcrExtensionHost.ExtensionDirectory))
                Assert.Ignore("测试目录下已存在扩展，无法验证“未安装”分支");

            var step = new GetInputStep();
            Assert.That(step.InputModes, Does.Not.Contain(GetInputMode.OCR), "没有 OCR 扩展时不该提供 OCR 模式");
            Assert.That(step.InputModes, Does.Contain(GetInputMode.ScreenText));
            Assert.That(step.IsOcrAvailable, Is.False);
            Assert.That(step.OcrUnavailableHint, Is.Not.Null.And.Not.Empty);
            Assert.That(step.OcrUnavailableVisibility, Is.EqualTo(System.Windows.Visibility.Visible));
        }

        [Test]
        public void GetInputStep_WithExtension_OffersOcrMode()
        {
            OcrExtensionHost.ResetForTests();
            OcrExtensionHost.SetFactoryForTests(new FakeFactory());

            var step = new GetInputStep();
            Assert.That(step.InputModes, Does.Contain(GetInputMode.OCR));
            Assert.That(step.IsOcrAvailable, Is.True);
            Assert.That(step.OcrUnavailableVisibility, Is.EqualTo(System.Windows.Visibility.Collapsed));
        }

        [Test]
        public void Host_FakeExtension_DelegatesRecognition()
        {
            OcrExtensionHost.ResetForTests();
            OcrExtensionHost.SetFactoryForTests(new FakeFactory { Result = "识别成功" });

            Assert.That(OcrExtensionHost.IsAvailable, Is.True);
            Assert.That(OcrExtensionHost.Description, Does.Contain("Fake"));
            Assert.That(OcrExtensionHost.RecognizePng(new byte[] { 1, 2, 3 }), Is.EqualTo("识别成功"));
        }

        [Test]
        public void Host_FakeExtensionThrows_RecognitionReturnsNullWithoutThrowing()
        {
            OcrExtensionHost.ResetForTests();
            OcrExtensionHost.SetFactoryForTests(new FakeFactory { ThrowOnRecognize = true });

            string result = null;
            Assert.DoesNotThrow((TestDelegate)(() => result = OcrExtensionHost.RecognizePng(new byte[] { 1, 2, 3 })));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Host_FactoryThrows_IsUnavailableWithEngineReason()
        {
            OcrExtensionHost.ResetForTests();
            OcrExtensionHost.SetFactoryForTests(new FakeFactory { ThrowOnCreate = true });

            Assert.That(OcrExtensionHost.IsAvailable, Is.False, "引擎创建失败时 OCR 必须算不可用");
            Assert.That(OcrExtensionHost.UnavailableReason, Does.Contain("tessdata"));
        }

        [Test]
        public void Host_RealExtension_RecognizesText()
        {
            string extensionDir = FindBuiltExtensionDirectory();
            if (extensionDir == null)
                Assert.Ignore("未构建 OCR 扩展（OcrExtension\\bin\\x64\\Debug 不存在），跳过端到端识别用例");

            OcrExtensionHost.ResetForTests();
            OcrExtensionHost.ExtensionDirectoryOverride = extensionDir;

            Assert.That(OcrExtensionHost.IsAvailable, Is.True,
                $"扩展应当可用：{OcrExtensionHost.UnavailableReason}");
            Assert.That(OcrExtensionHost.Description, Does.Contain("Tesseract"));

            string text = OcrExtensionHost.RecognizePng(CreateTextPng("HELLO OCR 123"));
            Assert.That(text, Is.Not.Null);
            Assert.That(text.ToUpperInvariant().Replace(" ", string.Empty), Does.Contain("HELLO"),
                $"识别结果不符：{text}");
        }

        private sealed class FakeFactory : IOcrExtensionFactory
        {
            public string Result { get; set; } = string.Empty;
            public bool ThrowOnCreate { get; set; }
            public bool ThrowOnRecognize { get; set; }

            public string Name => "Fake OCR";

            public string Version => "1.0.0";

            public IOcrEngine CreateEngine()
            {
                if (ThrowOnCreate)
                    throw new InvalidOperationException("缺少语言数据 tessdata");
                return new FakeEngine(Result, ThrowOnRecognize);
            }
        }

        private sealed class FakeEngine : IOcrEngine
        {
            private readonly string _result;
            private readonly bool _throw;

            public FakeEngine(string result, bool shouldThrow)
            {
                _result = result;
                _throw = shouldThrow;
            }

            public string Name => "Fake OCR";

            public string RecognizePng(byte[] pngBytes)
            {
                if (_throw) throw new InvalidOperationException("识别失败");
                return _result;
            }

            public void Dispose() { }
        }
    }
}
