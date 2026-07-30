using PaddleOCRSharp;
using System;
using System.Drawing;

namespace ShaoLu.Services
{
    /// <summary>
    /// OCR 服务封装，基于 PaddleOCRSharp
    /// </summary>
    public class OCRService
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        private static PaddleOCREngine _ocr;
        private static readonly object _lock = new object();

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
                            // 使用默认配置（中英文混合识别）
                            OCRModelConfig config = null; // null 使用默认模型
                            OCRParameter parameter = new OCRParameter();
                            _ocr = new PaddleOCREngine(config, parameter);
                            logger.Info("PaddleOCR initialized successfully");
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Failed to initialize PaddleOCR");
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
        /// <returns>识别到的文本，多行用换行符分隔</returns>
        public static string Recognize(Bitmap bmp)
        {
            if (bmp == null)
                return string.Empty;

            Init();

            try
            {
                OCRResult result = _ocr.DetectText(bmp);
                if (result == null || result.TextBlocks == null || result.TextBlocks.Count == 0)
                    return string.Empty;

                // 将所有文本块合并
                var texts = new System.Text.StringBuilder();
                for (int i = 0; i < result.TextBlocks.Count; i++)
                {
                    if (i > 0) texts.AppendLine();
                    texts.Append(result.TextBlocks[i].Text);
                }
                return texts.ToString();
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
                // 获取 DPI 缩放因子，将逻辑像素转换为物理像素
                double dpiScaleX = 1.0, dpiScaleY = 1.0;
                var mainWindow = System.Windows.Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(mainWindow);
                    dpiScaleX = dpi.DpiScaleX;
                    dpiScaleY = dpi.DpiScaleY;
                }

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
