using ShaoLu.Services.Ocr;
using System;
using System.IO;
using System.Linq;
using TesseractOCR;
using TesseractOCR.Enums;

namespace ShaoLu.OcrExtension
{
    /// <summary>
    /// 基于 TesseractOCR 的 OCR 引擎实现。
    ///
    /// 与主程序约定：主程序把屏幕区域截成位图后编码成 PNG 字节传进来，这里只负责识别文字。
    ///
    /// 一个必须处理的细节：TesseractOCR 的原生加载器（InteropDotNet）只按
    /// “当前工作目录\x64”和“程序目录\x64”找 tesseract55.dll / leptonica。
    /// 扩展放在 extensions\Ocr 下时这两处都不成立，因此调用前后把当前工作目录临时切到扩展目录，
    /// 调用完立刻还原（原生库只会解析一次，后续调用同样走这段保护，开销只有两次系统调用）。
    /// </summary>
    public sealed class TesseractOcrEngine : IOcrEngine
    {
        /// <summary>识别语言：简体中文 + 英文（tessdata 里没有 chi_sim 时会退化为只识别英文）</summary>
        public const string PreferredLanguage = "chi_sim+eng";
        public const string FallbackLanguage = "eng";

        private readonly object _sync = new object();
        private readonly string _rootDir;
        private readonly string _tessDataPath;
        private Engine _engine;
        private string _language;
        private bool _disposed;

        public TesseractOcrEngine()
        {
            _rootDir = Path.GetDirectoryName(typeof(TesseractOcrEngine).Assembly.Location);
            if (string.IsNullOrEmpty(_rootDir))
                throw new InvalidOperationException("OCR 扩展目录无法确定。");

            _tessDataPath = Path.Combine(_rootDir, "tessdata");
            if (!Directory.Exists(_tessDataPath))
                throw new InvalidOperationException(
                    $"OCR 扩展缺少语言数据目录：{_tessDataPath}（扩展包里的 tessdata 需要一起复制）。");

            if (!Directory.GetFiles(_tessDataPath, "*.traineddata").Any())
                throw new InvalidOperationException(
                    $"OCR 扩展的语言数据目录是空的：{_tessDataPath}（缺少 *.traineddata）。");

            string nativeDir = Path.Combine(_rootDir, "x64");
            if (!Directory.Exists(nativeDir))
                throw new InvalidOperationException(
                    $"OCR 扩展缺少原生库目录：{nativeDir}（需要 x64\\tesseract55.dll 与 x64\\leptonica-1.85.0.dll）。");

            EnsureEngine();
        }

        public string Name => string.IsNullOrEmpty(_language)
            ? "Tesseract OCR"
            : $"Tesseract OCR [{_language}]";

        public string RecognizePng(byte[] pngBytes)
        {
            if (pngBytes == null || pngBytes.Length == 0)
                return string.Empty;

            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(TesseractOcrEngine));
                EnsureEngine();

                return WithExtensionWorkingDirectory(() =>
                {
                    using (var pix = TesseractOCR.Pix.Image.LoadFromMemory(pngBytes))
                    using (var page = _engine.Process(pix))
                    {
                        return page.Text?.Trim() ?? string.Empty;
                    }
                });
            }
        }

        private void EnsureEngine()
        {
            if (_engine != null) return;

            WithExtensionWorkingDirectory<object>(() =>
            {
                // 语言包可能被裁减过：先试中文+英文，缺 chi_sim 时退化为纯英文
                foreach (var language in new[] { PreferredLanguage, FallbackLanguage })
                {
                    if (language == PreferredLanguage && !HasLanguageData("chi_sim"))
                        continue;

                    try
                    {
                        _engine = new Engine(_tessDataPath, language, EngineMode.Default);
                        _language = language;
                        return null;
                    }
                    catch (Exception) when (language == PreferredLanguage)
                    {
                        // 中文模型缺失/损坏时继续尝试英文，两侧都失败才把异常抛给主程序
                    }
                }

                throw new InvalidOperationException(
                    $"OCR 引擎初始化失败：语言数据不可用（{_tessDataPath}）。");
            });
        }

        private bool HasLanguageData(string language)
        {
            string file = Path.Combine(_tessDataPath, language + ".traineddata");
            return File.Exists(file);
        }

        /// <summary>
        /// 在扩展目录作为“当前工作目录”的情况下执行动作（原生库只在扩展目录下能被找到）。
        /// 切换过程加锁串行，避免与其它线程的识别交错。
        /// </summary>
        private T WithExtensionWorkingDirectory<T>(Func<T> action)
        {
            lock (NativeSearchLock)
            {
                string original = TryGetCurrentDirectory();
                bool switched = false;
                try
                {
                    if (original == null || !string.Equals(original, _rootDir, StringComparison.OrdinalIgnoreCase))
                    {
                        Environment.CurrentDirectory = _rootDir;
                        switched = true;
                    }
                    return action();
                }
                finally
                {
                    if (switched && original != null)
                    {
                        try { Environment.CurrentDirectory = original; }
                        catch { /* 原目录已不存在时保持扩展目录，不影响后续识别 */ }
                    }
                }
            }
        }

        private static string TryGetCurrentDirectory()
        {
            try { return Environment.CurrentDirectory; }
            catch { return null; }
        }

        /// <summary>原生库解析期间的全局锁（静态：多个引擎实例也会共享同一份原生库）</summary>
        private static readonly object NativeSearchLock = new object();

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                try { _engine?.Dispose(); }
                catch { /* 退出过程中的释放异常不重要 */ }
                _engine = null;
            }
        }
    }
}
