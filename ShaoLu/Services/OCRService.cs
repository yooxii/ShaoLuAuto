using TesseractOCR;
using TesseractOCR.Enums;
using System;
using System.Drawing;
using System.IO;

namespace ShaoLu.Services
{
    /// <summary>
    /// OCR 服务封装，基于 TesseractOCR
    /// </summary>
    public class OCRService
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        private static Engine _ocr;
        private static readonly object _lock = new object();

        // 缓存 DPI 缩放因子，避免从后台线程访问 WPF 对象
        // 在应用启动时（UI 线程）设置一次，之后仅读取，无竞态风险
        private static double _cachedDpiX = 1.0;
        private static double _cachedDpiY = 1.0;

        /// <summary>缓存的 X 方向 DPI 缩放因子</summary>
        public static double CachedDpiX => _cachedDpiX;
        /// <summary>缓存的 Y 方向 DPI 缩放因子</summary>
        public static double CachedDpiY => _cachedDpiY;

        /// <summary>
        /// 在 UI 线程上更新 DPI 缓存（应在主窗口加载后调用）
        /// </summary>
        public static void UpdateDpi()
        {
            try
            {
                var mainWindow = System.Windows.Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(mainWindow);
                    _cachedDpiX = dpi.DpiScaleX;
                    _cachedDpiY = dpi.DpiScaleY;
                    logger.Info("DPI cache updated: X={0}, Y={1}", _cachedDpiX, _cachedDpiY);
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to update DPI cache, using default 1.0");
            }
        }

        /// <summary>
        /// 初始化 OCR 引擎（懒加载，首次调用时初始化）
        /// </summary>
        public static void Init()
        {
            if (_ocr == null)
            {
                lock (_lock)
                {
                    if (_ocr == null)
                    {
                        try
                        {
                            string tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
                            _ocr = new Engine(tessDataPath, "chi_sim+eng", EngineMode.Default);
                            logger.Info("Tesseract OCR initialized successfully, tessdata: {0}", tessDataPath);
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Failed to initialize Tesseract OCR");
                            throw;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 对 Bitmap 执行 OCR 识别，返回识别文本
        /// </summary>
        /// <param name="bmp">要识别的图像</param>
        /// <returns>识别到的文本</returns>
        public static string Recognize(Bitmap bmp)
        {
            if (bmp == null)
                return string.Empty;

            Init();

            try
            {
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    byte[] imgBytes = ms.ToArray();
                    using (var pix = TesseractOCR.Pix.Image.LoadFromMemory(imgBytes))
                    using (var page = _ocr.Process(pix))
                    {
                        return page.Text?.Trim() ?? string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "OCR recognition failed");
                return string.Empty;
            }
        }

        /// <summary>
        /// 截取屏幕指定区域并执行 OCR
        /// </summary>
        /// <param name="region">屏幕区域（WPF 逻辑像素坐标）</param>
        /// <returns>识别到的文本</returns>
        public static string RecognizeRegion(System.Windows.Rect region)
        {
            if (region.IsEmpty || region.Width <= 0 || region.Height <= 0)
                return string.Empty;

            try
            {
                // 使用缓存的 DPI 缩放因子，将逻辑像素转换为物理像素
                double dpiScaleX = _cachedDpiX;
                double dpiScaleY = _cachedDpiY;

                int x = (int)(region.X * dpiScaleX);
                int y = (int)(region.Y * dpiScaleY);
                int width = Math.Max(1, (int)(region.Width * dpiScaleX));
                int height = Math.Max(1, (int)(region.Height * dpiScaleY));

                using (Bitmap bmp = new Bitmap(width, height))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(x, y, 0, 0, new Size(width, height));
                    }
                    return Recognize(bmp);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Screen region OCR failed for region {0}", region);
                return string.Empty;
            }
        }

        /// <summary>
        /// 释放 OCR 引擎资源
        /// </summary>
        public static void Dispose()
        {
            lock (_lock)
            {
                _ocr?.Dispose();
                _ocr = null;
            }
        }
    }
}
