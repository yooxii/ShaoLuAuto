using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;
using WindowsInput;
using static ShaoLu.Models.AutoguiModel;

namespace ShaoLu.Utils
{
    public class Autogui
    {

        public enum Poisition
        {
            Center = 0,
            LeftTop = 1,
            RightTop = 2,
            LeftDown = 3,
            RightDown = 4
        }

        private static InputSimulator sim;

        /// <summary>
        /// 从屏幕中查找指定的图像，并返回其在屏幕上的位置和相似度。
        /// </summary>
        /// <param name="templateImage"> 要查找的图像 </param>
        /// <param name="threshold"> 相似度阈值 </param>
        /// <param name="gaptime"> 若没有找到，重复的间隔时间，(s) </param>
        /// <param name="timeout"> 若没有找到，超时时间，(s) </param>
        /// <returns>匹配结果，包含坐标和相似度</returns>
        public static Apoint FindImageOnScreen(Bitmap templateImage, double threshold = 0.8, double gaptime = 0.2, double timeout = 3)
        {
            // 将秒转换为毫秒，方便计时和休眠
            int gapTimeMs = (int)(gaptime * 1000);
            int timeoutMs = (int)(timeout * 1000);

            // 使用 Stopwatch 进行精确计时
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Apoint res = new();

            // 将模板转换为 Mat（只需转换一次，避免在循环中重复转换）
            using var templateMat = new Mat(BitmapConverter.ToMat(templateImage));
            using var grayTemplate = templateMat.CvtColor(ColorConversionCodes.BGR2GRAY);

            while (true)
            {
                // 1. 每次循环都重新获取屏幕截图（因为屏幕内容是动态变化的）
                using var screenImg = CaptureScreen();
                using var screenMat = new Mat(BitmapConverter.ToMat(screenImg));
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

                    res.Center = new OpenCvSharp.Point(centerX, centerY);
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

            // 超时未找到，返回包含最后一次相似度的默认 Apoint 对象
            return res;
        }

        /// <summary>
        /// 截取全屏
        /// </summary>
        /// <returns> Bitmap格式的全屏截图 </returns>
        public static Bitmap CaptureScreen()
        {
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            Bitmap bmpSrceen = new(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(bmpSrceen);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            return bmpSrceen;
        }

        public static void StartAuto()
        {
            sim = new();
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
        /// 0，中心；1，左上；2，右上；3，左下；4，右下
        /// </summary>
        /// <param name="point">给定的位置</param>
        /// <param name="poisition">0，中心；1，左上；2，右上；3，左下；4，右下</param>
        public static void MoveMouseTo(Apoint point, Poisition poisition = 0)
        {
            if (point.IsEmpty)
            {
                return;
            }
            switch (poisition)
            {
                case Poisition.Center:
                    MoveMouseTo(point.Center.X, point.Center.Y);
                    break;
                case Poisition.LeftTop:
                    MoveMouseTo(point.LeftTop.X, point.LeftTop.Y);
                    break;
                case Poisition.RightTop:
                    MoveMouseTo(point.RightTop.X, point.RightTop.Y);
                    break;
                case Poisition.LeftDown:
                    MoveMouseTo(point.LeftDown.X, point.LeftDown.Y);
                    break;
                case Poisition.RightDown:
                    MoveMouseTo(point.RightDown.X, point.RightDown.Y);
                    break;
                default:
                    MoveMouseTo(point.Center.X, point.Center.Y);
                    break;
            }
        }

        public static bool ClickImageOnScreen(Bitmap templateImage, Poisition clickpoi = 0, int clicks = 1, double clickgaptime = 0.1, double threshold = 0.8, double waittime = 0.1, double timeout = 3)
        {
            int waitTimeMs = (int)(waittime * 1000);
            int clickGapTimeMs = (int)(clickgaptime * 1000);
            int timeoutMs = (int)(timeout * 1000);

            // 如果设置了等待时间，先等待指定的时间再开始查找
            Thread.Sleep(waitTimeMs);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (true)
            {
                Apoint point = FindImageOnScreen(templateImage, threshold, 0.2, 0.4);
                if (!point.IsEmpty)
                {
                    MoveMouseTo(point, clickpoi);
                    for (int i = 0; i < clicks; i++)
                    {
                        sim.Mouse.LeftButtonClick();
                        Thread.Sleep(clickGapTimeMs); // 每次点击后等待
                    }
                    return true;
                }

                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                {
                    return false; // 超时退出
                }
            }
        }
    }
}
