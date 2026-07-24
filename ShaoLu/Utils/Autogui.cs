using OpenCvSharp;
using OpenCvSharp.Extensions;
using ShaoLu.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WindowsInput;
using ShaoLu.Models;
using Clipboard = System.Windows.Clipboard;
using Point = ShaoLu.Models.Point;

namespace ShaoLu.Utils
{
    public class AutoguiImage
    {
        public Bitmap Bitmap;
        public Autogui.Position Position;
        public double Threshold = 0.85;

    }

    public class Autogui
    {
        private readonly static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// 鼠标位置
        /// </summary>
        public enum Position
        {
            Center = 0,
            LeftTop = 1,
            RightTop = 2,
            LeftDown = 3,
            RightDown = 4
        }

        private static InputSimulator sim;
        public static void StartAuto()
        {
            sim = new();
        }

#nullable enable
        #region 图像类

        /// <summary>
        /// 从屏幕中查找指定的图像，并返回其在屏幕上的位置和相似度。
        /// </summary>
        /// <param name="templateImage"> 要查找的图像 </param>
        /// <param name="threshold"> 相似度阈值 </param>
        /// <param name="gaptime"> 若没有找到，重复的间隔时间，(s) </param>
        /// <param name="timeout"> 若没有找到，超时时间，(s) </param>
        /// <returns>匹配结果，包含坐标和相似度</returns>
        public static AutoRect FindImageOnScreen(Bitmap templateImage, double threshold = 0.8, double gaptime = 0.2, double timeout = 3)
        {
            if (templateImage == null)
            {
                return new();
            }
            // 将秒转换为毫秒，方便计时和休眠
            int gapTimeMs = (int)(gaptime * 1000);
            int timeoutMs = (int)(timeout * 1000);

            // 使用 Stopwatch 进行精确计时
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            AutoRect res = new();

            // 将模板转换为 Mat（只需转换一次，避免在循环中重复转换）
            using var templateMat = BitmapConverter.ToMat(templateImage);
            using var grayTemplate = templateMat.CvtColor(ColorConversionCodes.BGR2GRAY);

            while (true)
            {
                // 1. 每次循环都重新获取屏幕截图（因为屏幕内容是动态变化的）
                using var screenImg = CaptureScreen();
                using var screenMat = BitmapConverter.ToMat(screenImg);
                using var grayScreen = screenMat.CvtColor(ColorConversionCodes.BGR2GRAY);

                // 2. 执行模板匹配
                using Mat result = new();
                Cv2.MatchTemplate(grayScreen, grayTemplate, result, TemplateMatchModes.CCoeffNormed);

                // 3. 获取最大相似度及位置
                Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out OpenCvSharp.Point minLoc, out OpenCvSharp.Point maxLoc);

                // 4. 判断是否匹配成功
                if (maxVal >= threshold)
                {
                    // 计算中心坐标
                    int centerX = maxLoc.X + templateMat.Width / 2;
                    int centerY = maxLoc.Y + templateMat.Height / 2;

                    res.Center = new Point(centerX, centerY);
                    res.LeftTop = maxLoc;
                    res.Similarity = maxVal;
                    return res; // 找到目标，直接返回
                }

                // 5. 未找到目标，更新相似度并检查是否超时
                res.Similarity = maxVal;

                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                {
                    break; // 超时，退出循环
                }

                // 6. 等待指定的间隔时间后重试
                Thread.Sleep(gapTimeMs);
            }

            if (res.Similarity < threshold)
            {
                throw new Exception(LanguageService.GetLocalizedString("NoMatchingImage"));
            }
            // 超时未找到，返回包含最后一次相似度的默认 Apoint 对象
            return res;
        }

        public static List<AutoRect> FindImagesOnScreen(List<AutoguiImage> templateImage, double gaptime = 0.2, double timeout = 3, bool signle = false)
        {
            if (templateImage == null || templateImage.Count == 0)
            {
                throw new Exception(LanguageService.GetLocalizedString("No_img_Warning"));
            }

            List<AutoRect> apoints = [];
            int gapTimeMs = (int)(gaptime * 1000);
            int timeoutMs = (int)(timeout * 1000);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int i = 0;
            Bitmap? tempImage = null;
            while (true)
            {
                try
                {
                    AutoRect res = FindImageOnScreen(templateImage[i].Bitmap, templateImage[i].Threshold, 0, 0);
                    apoints.Add(res);
                    if (signle)
                    {
                        tempImage?.Dispose();
                        return apoints;
                    }
                    if (apoints.Count >= templateImage.Count)
                    {
                        tempImage?.Dispose();
                        return apoints;
                    }
                }
                catch (Exception ex)
                {
                    long elapsedMs = stopwatch.ElapsedMilliseconds;
                    if (elapsedMs >= timeoutMs)
                    {
                        tempImage?.Dispose();
                        throw ex;
                    }
                }
                Thread.Sleep(gapTimeMs);
                i++;
                if (i >= templateImage.Count)
                {
                    i = 0;
                }
            }
        }

        /// <summary>
        /// 截取全屏
        /// </summary>
        /// <returns> Bitmap格式的全屏截图 </returns>
        public static Bitmap CaptureScreen()
        {
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            Bitmap bmpSrceen = new(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(bmpSrceen);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            return bmpSrceen;
        }

        /// <summary>
        /// 将鼠标移动到指定的屏幕坐标位置。
        /// </summary>
        /// <param name="x"> 横坐标 </param>
        /// <param name="y"> 纵坐标 </param>
        public static void MoveMouseTo(int x, int y)
        {
            sim.Mouse.MoveMouseToPositionOnVirtualDesktop(x * 65535 / Screen.PrimaryScreen.Bounds.Width, y * 65535 / Screen.PrimaryScreen.Bounds.Height);
        }

        /// <summary>
        /// 将鼠标移动到指定的Apoint位置。
        /// </summary>
        /// <param name="rect">给定的位置区域</param>
        /// <param name="position">锚点位置：0-中心, 1-左上, 2-右上, 3-左下, 4-右下</param>
        /// <param name="clickoffset">相对于锚点的像素偏移量</param>
        public static void MoveMouseTo(AutoRect rect, Position position = Position.Center, Point? clickoffset = null)
        {
            // 1. 防御性检查：如果点无效或为空，直接返回
            if (rect == null || rect.IsEmpty)
            {
                return;
            }

            int targetX;
            int targetY;

            // 2. 根据锚点类型确定基准坐标
            switch (position)
            {
                case Position.Center:
                    targetX = rect.Center.X;
                    targetY = rect.Center.Y;
                    break;
                case Position.LeftTop:
                    targetX = rect.LeftTop.X;
                    targetY = rect.LeftTop.Y;
                    break;
                case Position.RightTop:
                    targetX = rect.RightTop.X;
                    targetY = rect.RightTop.Y;
                    break;
                case Position.LeftDown:
                    targetX = rect.LeftDown.X;
                    targetY = rect.LeftDown.Y;
                    break;
                case Position.RightDown:
                    targetX = rect.RightDown.X;
                    targetY = rect.RightDown.Y;
                    break;
                default:
                    targetX = rect.Center.X;
                    targetY = rect.Center.Y;
                    break;
            }

            // 3. 应用偏移量
            if (!(clickoffset?.IsEmpty ?? true))
            {
                targetX += clickoffset.X;
                targetY += clickoffset.Y;
            }

            // 4. 执行移动
            // 注意：此处假设 MoveMouseTo(double, double) 是已存在的底层实现
            MoveMouseTo(targetX, targetY);
        }

        public static bool ClickImageOnScreen(Bitmap templateImage, Position position = 0, List<Point>? clickposition = null, double threshold = 0.8, int clicks = 1, double clickgaptime = 0.1, double nextclicktime = 0.1, double waittime = 0.1, double timeout = 3)
        {
            int nextclickTimeMs = (int)(nextclicktime * 1000);
            int waitTimeMs = (int)(waittime * 1000);
            int clickGapTimeMs = (int)(clickgaptime * 1000);
            int timeoutMs = (int)(timeout * 1000);

            // 如果设置了等待时间，先等待指定的时间再开始查找
            Thread.Sleep(waitTimeMs);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (true)
            {
                AutoRect rect = FindImageOnScreen(templateImage, threshold, 0.2, 0.4);
                if (!rect.IsEmpty && clickposition != null)
                {
                    foreach (Point p in clickposition)
                    {
                        MoveMouseTo(rect, position, p);
                        for (int i = 0; i < clicks; i++)
                        {
                            sim.Mouse.LeftButtonClick();
                            Thread.Sleep(clickGapTimeMs); // 点击间隔
                        }
                        Thread.Sleep(nextclickTimeMs); // 等待下一次点击
                    }
                    return true;
                }

                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                {
                    return false; // 超时退出
                }
            }
        }

        /// <summary>
        /// 将 WPF ImageSource 转换为 System.Drawing.Bitmap
        /// </summary>
        public static Bitmap? ConvertImageSourceToBitmap(ImageSource imageSource)
        {
            if (imageSource == null) return null;

            // 如果是 BitmapSource，可以直接处理
            if (imageSource is BitmapSource bitmapSource)
            {
                try
                {
                    // 创建一个新的 Bitmap
                    int width = bitmapSource.PixelWidth;
                    int height = bitmapSource.PixelHeight;

                    // 确保格式支持转换，通常转为 Bgra32 或 Pbgra32
                    // 如果源格式不是标准格式，可能需要先转换格式
                    var format = bitmapSource.Format;

                    // 使用 InteropBitmap 进行高效转换
                    // 注意：CopyPixels 需要unsafe代码或者使用 Marshal，这里使用更安全的 Interop 方式

                    using var stream = new System.IO.MemoryStream();
                    // 编码为 PNG 或 BMP 到内存流
                    BitmapEncoder encoder = new PngBitmapEncoder(); // 或者 BmpBitmapEncoder
                    encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                    encoder.Save(stream);

                    // 从流中创建 GDI+ Bitmap
                    stream.Position = 0;
                    return new Bitmap(stream);
                }
                catch (Exception ex)
                {
                    // 记录日志或处理异常
                    logger.Error(ex, "Failed to convert ImageSource to Bitmap.");
                    return null;
                }
            }

            return null;
        }
        #endregion

        #region 文字类

        public static bool TypeText(string text, int delayBetweenKeys = 0)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new Exception("TypeText is Null.");
            }

            for (int i = 0; i < text.Length; i++)
            {
                char key = text[i];
                sim.Keyboard.TextEntry(key);
                Thread.Sleep(delayBetweenKeys);
            }
            return true;
        }

        private static bool ExecuteClipboardOperation(string text, int delayBeforePaste)
        {
            string? originalClipboard = null;
            try
            {
                // 1. 备份当前剪贴板内容 (使用 WPF 的 Clipboard)
                if (Clipboard.ContainsText())
                {
                    originalClipboard = Clipboard.GetText();
                }

                // 2. 设置剪贴板
                Clipboard.SetText(text);

                // 3. 等待剪贴板更新
                Thread.Sleep(delayBeforePaste);

                // 4. 模拟 Ctrl+V
                sim.Keyboard.KeyDown(WindowsInput.Native.VirtualKeyCode.CONTROL);
                sim.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.VK_V);
                sim.Keyboard.KeyUp(WindowsInput.Native.VirtualKeyCode.CONTROL);

                return true;
            }
            finally
            {
                // 5. 恢复剪贴板
                if (originalClipboard != null)
                {
                    try
                    {
                        Clipboard.SetText(originalClipboard);
                    }
                    catch { /* 忽略恢复失败的错误 */ }
                }
            }
        }

        public static bool TypeTextSafe(string text, int delayBeforePaste = 50)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new Exception("TypeText is Null.");
            }

            try
            {
                // 关键：确保在 STA 线程（通常是 UI 线程）上执行剪贴板操作
                if (App.Current.Dispatcher.CheckAccess())
                {
                    return ExecuteClipboardOperation(text, delayBeforePaste);
                }
                else
                {
                    // 如果在后台线程，同步Invoke到UI线程
                    // 注意：InvokeAsync 是异步的，这里需要等待结果，所以用 Invoke
                    var result = App.Current.Dispatcher.Invoke(() =>
                        ExecuteClipboardOperation(text, delayBeforePaste));
                    return result;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to type text via clipboard.");
                return false;
            }
        }

        #endregion
    }
}
