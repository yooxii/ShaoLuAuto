using OpenCvSharp;
using OpenCvSharp.Extensions;
using ShaoLu.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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

    /// <summary>
    /// 颜色（高精度）匹配结果，由纯函数 <see cref="Autogui.MatchImageColor"/> 输出
    /// </summary>
    public struct ColorMatchResult
    {
        /// <summary>
        /// 结构相似度与颜色相似度同时达标时为 true
        /// </summary>
        public bool IsMatch { get; set; }

        /// <summary>
        /// 模板左上角在目标图中的坐标（颜色模式下为邻域校正后的最佳位置）
        /// </summary>
        public System.Drawing.Point LeftTop { get; set; }

        /// <summary>
        /// 灰度结构相似度（CCoeffNormed 最大值）
        /// </summary>
        public double StructureSimilarity { get; set; }

        /// <summary>
        /// 最佳位置处的颜色相似度（0~1）
        /// </summary>
        public double ColorSimilarity { get; set; }
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
        /// 虚拟桌面边界（含副屏，坐标可能为负）。
        /// 截图、模板匹配结果换算与鼠标坐标归一化必须统一使用该坐标系，
        /// 否则副屏上的目标永远匹配不到、点击位置也会整体偏移。
        /// 注意：ScreenCoordinates 里的“相对坐标”仍以主屏尺寸为基准（用户语义），两者互不影响。
        /// </summary>
        internal static Rectangle GetVirtualScreenBounds() => SystemInformation.VirtualScreen;

        /// <summary>
        /// 从屏幕中查找指定的图像，并返回其在屏幕上的位置和相似度。
        /// </summary>
        /// <param name="templateImage"> 要查找的图像 </param>
        /// <param name="threshold"> 相似度阈值 </param>
        /// <param name="gaptime"> 若没有找到，重复的间隔时间，(s) </param>
        /// <param name="timeout"> 若没有找到，超时时间，(s) </param>
        /// <param name="highPrecision"> 是否启用颜色（高精度）匹配；false 时走原有灰度路径，行为与历史版本一致 </param>
        /// <param name="colorThreshold"> 颜色相似度阈值，仅 highPrecision 为 true 时生效 </param>
        /// <returns>匹配结果，包含坐标和相似度；未找到时返回空结果（IsEmpty=true），由调用方按 FalseGoto 处理</returns>
        public static AutoRect FindImageOnScreen(Bitmap templateImage, double threshold = 0.8, double gaptime = 0.2, double timeout = 3, bool highPrecision = false, double colorThreshold = 0.95)
        {
            if (templateImage == null)
            {
                return new();
            }
            // 截图覆盖整个虚拟桌面，匹配结果需要加回虚拟桌面原点才是屏幕绝对坐标
            Rectangle screenBounds = GetVirtualScreenBounds();
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

                double maxVal;
                OpenCvSharp.Point maxLoc;
                double colorSimilarity = 1.0;

                if (highPrecision)
                {
                    // 颜色（高精度）模式：灰度定位 + Lab 颜色校验，两者同时达标才算命中
                    var colorMatch = MatchImageColor(templateImage, screenImg, threshold, colorThreshold);
                    maxVal = colorMatch.StructureSimilarity;
                    maxLoc = new OpenCvSharp.Point(colorMatch.LeftTop.X, colorMatch.LeftTop.Y);
                    colorSimilarity = colorMatch.ColorSimilarity;
                }
                else
                {
                    // 原有灰度路径，未启用颜色模式时与历史版本保持一致
                    using var screenMat = BitmapConverter.ToMat(screenImg);
                    using var grayScreen = screenMat.CvtColor(ColorConversionCodes.BGR2GRAY);

                    // 2. 执行模板匹配
                    using Mat result = new();
                    Cv2.MatchTemplate(grayScreen, grayTemplate, result, TemplateMatchModes.CCoeffNormed);

                    // 3. 获取最大相似度及位置
                    Cv2.MinMaxLoc(result, out double minVal, out double grayMaxVal, out OpenCvSharp.Point minLoc, out OpenCvSharp.Point grayMaxLoc);
                    maxVal = grayMaxVal;
                    maxLoc = grayMaxLoc;
                }

                // 4. 判断是否匹配成功：颜色模式要求结构相似度与颜色相似度同时达标
                bool matched = highPrecision
                    ? maxVal >= threshold && colorSimilarity >= colorThreshold
                    : maxVal >= threshold;
                // 命中时 Similarity 取结构相似度与颜色相似度的较小值，保证“相似度”仍单调可信
                double similarity = highPrecision ? Math.Min(maxVal, colorSimilarity) : maxVal;

                if (matched)
                {
                    // 4.1 把匹配位置从“截图内部坐标”换算回“屏幕绝对坐标”（补上虚拟桌面原点，副屏可能是负坐标）
                    int left = screenBounds.X + maxLoc.X;
                    int top = screenBounds.Y + maxLoc.Y;
                    // 计算中心坐标
                    int centerX = left + templateMat.Width / 2;
                    int centerY = top + templateMat.Height / 2;

                    res.Center = new Point(centerX, centerY);
                    res.LeftTop = new Point(left, top);
                    res.Similarity = similarity;
                    return res; // 找到目标，直接返回
                }

                // 5. 未找到目标，更新相似度并检查是否超时
                res.Similarity = similarity;

                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                {
                    break; // 超时，退出循环
                }

                // 6. 等待指定的间隔时间后重试
                Thread.Sleep(gapTimeMs);
            }

            // 未匹配属于正常业务结果（图片没在屏幕上），返回空结果让步骤走 FalseGoto；
            // 真实异常（图像格式、参数、IO 等）仍由上面的匹配流程直接抛出
            return res;
        }

        /// <summary>
        /// 纯函数匹配核心：先用灰度归一化互相关定位最佳位置，再在 Lab 空间比较该位置的颜色。
        /// 不依赖屏幕截图，可脱离真实屏幕做单元测试；<see cref="FindImageOnScreen"/> 只是它的截屏 + 轮询外壳。
        /// </summary>
        /// <param name="templateImage"> 模板图像 </param>
        /// <param name="screenImage"> 目标图像（如屏幕截图） </param>
        /// <param name="threshold"> 灰度结构相似度阈值 </param>
        /// <param name="colorThreshold"> 颜色相似度阈值 </param>
        /// <param name="neighborhood"> 颜色比较的邻域半径（像素），用于抵消半像素 / 抗锯齿误差 </param>
        /// <returns> 命中与否、命中位置、结构相似度、颜色相似度 </returns>
        public static ColorMatchResult MatchImageColor(Bitmap templateImage, Bitmap screenImage, double threshold = 0.8, double colorThreshold = 0.95, int neighborhood = 1)
        {
            ColorMatchResult match = new();
            if (templateImage == null || screenImage == null)
            {
                return match;
            }

            using var templateMat = BitmapConverter.ToMat(templateImage);
            using var screenMat = BitmapConverter.ToMat(screenImage);
            if (templateMat.Width > screenMat.Width || templateMat.Height > screenMat.Height)
            {
                return match;
            }

            // 1. 灰度结构定位：与灰度模式使用同一种度量，保证 Similarity 可以横向比较
            using var grayTemplate = templateMat.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var grayScreen = screenMat.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var result = new Mat();
            Cv2.MatchTemplate(grayScreen, grayTemplate, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);
            match.StructureSimilarity = maxVal;

            // 2. 颜色校验：Lab 空间逐像素比较，色相差异不会被亮度/结构项掩盖
            using var labTemplate = templateMat.CvtColor(ColorConversionCodes.BGR2Lab);
            using var labScreen = screenMat.CvtColor(ColorConversionCodes.BGR2Lab);

            int radius = Math.Max(0, neighborhood);
            double bestColor = -1;
            OpenCvSharp.Point bestLoc = maxLoc;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = maxLoc.X + dx;
                    int y = maxLoc.Y + dy;
                    if (x < 0 || y < 0 || x + templateMat.Width > screenMat.Width || y + templateMat.Height > screenMat.Height)
                    {
                        continue;
                    }

                    double similarity = ComputeColorSimilarity(labTemplate, labScreen, x, y);
                    if (similarity > bestColor)
                    {
                        bestColor = similarity;
                        bestLoc = new OpenCvSharp.Point(x, y);
                    }
                }
            }

            match.ColorSimilarity = bestColor < 0 ? 0 : bestColor;
            match.LeftTop = new System.Drawing.Point(bestLoc.X, bestLoc.Y);
            match.IsMatch = maxVal >= threshold && match.ColorSimilarity >= colorThreshold;
            return match;
        }

        /// <summary>
        /// Lab 空间逐像素平均绝对差换算出的颜色相似度（0~1）：像素完全一致为 1.0。
        /// 亮度与两个色度通道等权平均，单通道量程取 255。
        /// </summary>
        private static double ComputeColorSimilarity(Mat labTemplate, Mat labScreen, int x, int y)
        {
            using var screenPatch = new Mat(labScreen, new Rect(x, y, labTemplate.Width, labTemplate.Height));
            using var diff = new Mat();
            Cv2.Absdiff(labTemplate, screenPatch, diff);
            Scalar mean = Cv2.Mean(diff);
            double meanAbsDiff = (mean.Val0 + mean.Val1 + mean.Val2) / 3.0;
            return 1.0 - meanAbsDiff / 255.0;
        }

        public static List<AutoRect> FindImagesOnScreen(List<AutoguiImage> templateImage, double gaptime = 0.2, double timeout = 3, bool signle = false)
        {
            if (templateImage == null || templateImage.Count == 0)
            {
                throw new Exception(LanguageService.GetLocalizedString("No_img_Warning"));
            }

            // 按下标保存每张模板的匹配结果，保证返回值与模板顺序一一对应
            AutoRect[] results = new AutoRect[templateImage.Count];
            int found = 0;
            int gapTimeMs = (int)(gaptime * 1000);
            int timeoutMs = (int)(timeout * 1000);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int i = 0;
            Exception? lastError = null;
            while (true)
            {
                try
                {
                    // 未匹配时 FindImageOnScreen 返回空结果（不再抛异常），只有真正匹配才计入
                    AutoRect res = FindImageOnScreen(templateImage[i].Bitmap, templateImage[i].Threshold, 0, 0);
                    if (!res.IsEmpty && results[i] == null)
                    {
                        results[i] = res;
                        found++;
                    }
                    if (signle && found > 0)
                    {
                        return results.Select(r => r ?? new AutoRect()).ToList();
                    }
                    if (found >= templateImage.Count)
                    {
                        return results.ToList();
                    }
                }
                catch (Exception ex)
                {
                    // 保留重试，但记录最内层异常用于区分“未找到”与“真实异常”
                    lastError = ex;
                }

                // 至少尝试一轮后才能判断超时：Timeout=0 表示只尝试一次，不再一进来就失败
                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                {
                    if (lastError != null)
                    {
                        throw new Exception(
                            $"{LanguageService.GetLocalizedString("NoMatchingImage")}: {lastError.Message}", lastError);
                    }
                    // 未找到的模板用空结果占位，保持与模板顺序一致
                    return results.Select(r => r ?? new AutoRect()).ToList();
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
        /// 截取全屏（整个虚拟桌面，含副屏；副屏在主屏左侧/上侧时截取原点为负坐标）
        /// </summary>
        /// <returns> Bitmap格式的全屏截图 </returns>
        public static Bitmap CaptureScreen()
        {
            Rectangle bounds = GetVirtualScreenBounds();
            Bitmap bmpSrceen = new(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(bmpSrceen);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            return bmpSrceen;
        }

        /// <summary>
        /// 截取屏幕指定区域
        /// </summary>
        /// <param name="region">屏幕绝对坐标区域（与 CaptureScreen 同一坐标系，即虚拟桌面像素，含负坐标）</param>
        /// <returns> 截取的 Bitmap，若区域为空或非法则返回 null </returns>
        public static Bitmap CaptureScreenRegion(Rectangle region)
        {
            if (region.Width <= 0 || region.Height <= 0) return null;
            try
            {
                // 裁剪范围必须是虚拟桌面（多屏），只按主屏裁剪会让副屏区域永远截不到
                var bounds = GetVirtualScreenBounds();
                // 将区域裁剪到屏幕范围内，防止越界
                region = Rectangle.Intersect(region, bounds);
                if (region.Width <= 0 || region.Height <= 0) return null;

                Bitmap bmp = new(region.Width, region.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using Graphics g = Graphics.FromImage(bmp);
                g.CopyFromScreen(region.X, region.Y, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 将鼠标移动到指定的屏幕坐标位置。
        /// </summary>
        /// <param name="x"> 横坐标（虚拟桌面绝对坐标，副屏可能为负） </param>
        /// <param name="y"> 纵坐标（同上） </param>
        public static void MoveMouseTo(int x, int y)
        {
            // 归一化基准必须是整个虚拟桌面，且要扣除虚拟桌面原点：
            // 原来按主屏尺寸归一化，副屏（负坐标/主屏之外）会整体偏移甚至落到屏幕外
            Rectangle bounds = GetVirtualScreenBounds();
            int maxX = Math.Max(1, bounds.Width - 1);
            int maxY = Math.Max(1, bounds.Height - 1);
            int normalizedX = Math.Max(0, Math.Min(65535, (x - bounds.X) * 65535 / maxX));
            int normalizedY = Math.Max(0, Math.Min(65535, (y - bounds.Y) * 65535 / maxY));
            sim.Mouse.MoveMouseToPositionOnVirtualDesktop(normalizedX, normalizedY);
        }

        /// <summary>
        /// 将鼠标移动到指定的Apoint位置。
        /// </summary>
        /// <param name="rect">给定的位置区域</param>
        /// <param name="position">锚点位置：0-中心, 1-左上, 2-右上, 3-左下, 4-右下</param>
        /// <param name="clickoffset">相对于锚点的像素偏移量</param>
        public static void MoveMouseTo(AutoRect rect, Position position = Position.Center, Point? clickoffset = null)
        {
            var target = ComputeScreenTarget(rect, position, clickoffset);
            if (target.HasValue)
                MoveMouseTo(target.Value.X, target.Value.Y);
        }

        /// <summary>
        /// 计算锚点+偏移对应的屏幕物理坐标；rect 无效时返回 null
        /// </summary>
        public static System.Drawing.Point? ComputeScreenTarget(AutoRect rect, Position position = Position.Center, Point? clickoffset = null)
        {
            // 1. 防御性检查：如果点无效或为空，直接返回
            if (rect == null || rect.IsEmpty)
            {
                return null;
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

            return new System.Drawing.Point(targetX, targetY);
        }

        public static bool ClickImageOnScreen(Bitmap templateImage, Position position = 0, List<Point>? clickposition = null, double threshold = 0.8, int clicks = 1, double clickgaptime = 0.1, double nextclicktime = 0.1, double waittime = 0.1, double timeout = 3)
        {
            var rect = ClickImageOnScreenEx(templateImage, position, clickposition, threshold, clicks, clickgaptime, nextclicktime, waittime, timeout);
            return !rect.IsEmpty;
        }

        /// <summary>
        /// 在屏幕上查找并点击图像，返回匹配结果（包含相似度和位置）
        /// </summary>
        public static AutoRect ClickImageOnScreenEx(Bitmap templateImage, Position position = 0, List<Point>? clickposition = null, double threshold = 0.8, int clicks = 1, double clickgaptime = 0.1, double nextclicktime = 0.1, double waittime = 0.1, double timeout = 3, bool highPrecision = false, double colorThreshold = 0.95)
        {
            int nextclickTimeMs = (int)(nextclicktime * 1000);
            int waitTimeMs = (int)(waittime * 1000);
            int clickGapTimeMs = (int)(clickgaptime * 1000);
            int timeoutMs = (int)(timeout * 1000);

            // 如果设置了等待时间，先等待指定的时间再开始查找
            Thread.Sleep(waitTimeMs);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            // 记录最内层异常：用于区分“未找到”与“真实异常（IO/参数/OpenCV）”
            Exception? lastError = null;

            while (true)
            {
                try
                {
                    AutoRect rect = FindImageOnScreen(templateImage, threshold, 0.1, 0.2, highPrecision, colorThreshold);
                    if (!rect.IsEmpty)
                    {
                        if (clickposition != null && clickposition.Count > 0)
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
                        }
                        else
                        {
                            // 没有配置点击点时按匹配区域中心点击一次，避免“匹配成功却不点击”
                            MoveMouseTo(rect, Position.Center);
                            for (int i = 0; i < clicks; i++)
                            {
                                sim.Mouse.LeftButtonClick();
                                Thread.Sleep(clickGapTimeMs);
                            }
                        }
                        return rect;
                    }
                }
                catch (Exception ex)
                {
                    // 保留重试逻辑，但记录最内层异常，超时后把原因带进最终错误
                    lastError = ex;
                }

                // 至少先尝试一次再判断超时：Timeout=0 表示“只尝试一次”，不再一进来就报未找到
                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                {
                    break;
                }
            }

            string notFound = LanguageService.GetLocalizedString("NoMatchingImage");
            if (lastError != null)
            {
                throw new Exception($"{notFound}: {lastError.Message}", lastError);
            }
            throw new Exception(notFound);
        }

        /// <summary>
        /// 顺序执行鼠标动作列表
        /// </summary>
        /// <param name="actions">动作列表</param>
        /// <param name="resolveDragPath">拖拽路径解析回调（返回完整路径的屏幕物理坐标序列；不足两点时跳过）</param>
        public static void ExecuteMouseActions(System.Collections.Generic.List<Models.MouseActionItem> actions, Func<Models.MouseActionItem, System.Collections.Generic.List<System.Drawing.Point>> resolveDragPath = null)
        {
            if (actions == null || actions.Count == 0) return;
            foreach (var action in actions)
            {
                for (int i = 0; i < action.Count; i++)
                {
                    switch (action.ActionType)
                    {
                        case Models.MouseActionType.LeftClick:
                            sim.Mouse.LeftButtonClick();
                            break;
                        case Models.MouseActionType.RightClick:
                            sim.Mouse.RightButtonClick();
                            break;
                        case Models.MouseActionType.DoubleClick:
                            sim.Mouse.LeftButtonDoubleClick();
                            break;
                        case Models.MouseActionType.LeftPress:
                            // 长按：按下左键保持“间隔时间”后释放
                            sim.Mouse.LeftButtonDown();
                            Thread.Sleep((int)(action.Interval * 1000));
                            sim.Mouse.LeftButtonUp();
                            break;
                        case Models.MouseActionType.ScrollUp:
                            sim.Mouse.VerticalScroll(1);
                            break;
                        case Models.MouseActionType.ScrollDown:
                            sim.Mouse.VerticalScroll(-1);
                            break;
                        case Models.MouseActionType.Drag:
                            ExecuteDrag(action, resolveDragPath);
                            break;
                    }
                    Thread.Sleep((int)(action.Interval * 1000));
                }
            }
        }

        /// <summary>
        /// 执行一次拖拽：移动到首个路径点→按下左键→依次经过各路径点→释放左键
        /// </summary>
        private static void ExecuteDrag(Models.MouseActionItem action, Func<Models.MouseActionItem, System.Collections.Generic.List<System.Drawing.Point>> resolveDragPath)
        {
            if (resolveDragPath == null) return;

            var points = resolveDragPath(action);
            if (points == null || points.Count < 2) return; // 至少两点才能构成拖拽

            MoveMouseTo(points[0].X, points[0].Y);
            Thread.Sleep(50);
            sim.Mouse.LeftButtonDown();
            Thread.Sleep(50);
            for (int i = 1; i < points.Count; i++)
            {
                MoveMouseTo(points[i].X, points[i].Y);
                Thread.Sleep(30);
            }
            Thread.Sleep(50);
            sim.Mouse.LeftButtonUp();
        }

        /// <summary>
        /// 构建图像步骤的完整拖拽路径：按顺序经过各点击点，相邻点击点之间插入手绘轨迹点
        /// </summary>
        private static System.Collections.Generic.List<System.Drawing.Point> BuildImageDragPath(Models.MouseActionItem action, AutoRect rect, Position position, List<Point> clickposition)
        {
            var result = new System.Collections.Generic.List<System.Drawing.Point>();
            if (clickposition == null || clickposition.Count == 0) return result;

            var trajs = action.ClickDragTrajectories;
            for (int i = 0; i < clickposition.Count; i++)
            {
                var target = ComputeScreenTarget(rect, position, clickposition[i]);
                if (target.HasValue) result.Add(target.Value);

                // 相邻点击点之间的手绘轨迹（未绘制时自然直线连接）
                if (i < clickposition.Count - 1 && trajs != null && i < trajs.Count)
                {
                    foreach (var t in trajs[i])
                    {
                        var tp = ComputeScreenTarget(rect, position, t);
                        if (tp.HasValue) result.Add(tp.Value);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 在屏幕上查找图像并执行鼠标动作序列
        /// </summary>
        public static AutoRect ClickImageOnScreenEx(Bitmap templateImage, Position position, List<Point>? clickposition, double threshold, System.Collections.Generic.List<Models.MouseActionItem> actions, double nextclicktime = 0.1, double waittime = 0.1, double timeout = 3, bool highPrecision = false, double colorThreshold = 0.95)
        {
            int nextclickTimeMs = (int)(nextclicktime * 1000);
            int waitTimeMs = (int)(waittime * 1000);
            int timeoutMs = (int)(timeout * 1000);

            Thread.Sleep(waitTimeMs);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            // 记录最内层异常：用于区分“未找到”与“真实异常（IO/参数/动作执行）”
            Exception? lastError = null;

            while (true)
            {
                try
                {
                    AutoRect rect = FindImageOnScreen(templateImage, threshold, 0.1, 0.2, highPrecision, colorThreshold);
                    if (!rect.IsEmpty)
                    {
                        if (clickposition != null && clickposition.Count > 0)
                        {
                            foreach (Point p in clickposition)
                            {
                                MoveMouseTo(rect, position, p);
                                ExecuteMouseActions(actions, act => BuildImageDragPath(act, rect, position, clickposition));
                                Thread.Sleep(nextclickTimeMs);
                            }
                        }
                        else
                        {
                            // 没有配置点击点时按匹配区域中心执行动作，避免“匹配成功却不点击”
                            MoveMouseTo(rect, Position.Center);
                            var centerOffset = new List<Point> { new Point(0, 0) };
                            ExecuteMouseActions(actions, act => BuildImageDragPath(act, rect, Position.Center, centerOffset));
                        }
                        return rect;
                    }
                }
                catch (Exception ex)
                {
                    // 保留重试逻辑，但记录最内层异常，超时后把原因带进最终错误
                    lastError = ex;
                }

                // 至少先尝试一次再判断超时：Timeout=0 表示“只尝试一次”，不再一进来就报未找到
                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                {
                    break;
                }
            }

            string notFound = LanguageService.GetLocalizedString("NoMatchingImage");
            if (lastError != null)
            {
                throw new Exception($"{notFound}: {lastError.Message}", lastError);
            }
            throw new Exception(notFound);
        }

        /// <summary>
        /// 尽力而为地冻结 WPF 图像，使其可跨线程访问；无法冻结或冻结失败时保持原对象不变，不抛异常。
        /// 注意：读取未冻结对象的 IsFrozen / CanFreeze 本身也会做线程归属校验并抛异常，故整段都需要保护。
        /// </summary>
        public static void FreezeIfPossible(ImageSource imageSource)
        {
            if (!(imageSource is System.Windows.Freezable freezable)) return;

            try
            {
                if (freezable.IsFrozen || !freezable.CanFreeze) return;
                freezable.Freeze();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to freeze ImageSource.");
            }
        }

        /// <summary>
        /// 依次尝试各文件路径创建 Bitmap；用 FileStream 读入后立刻复制一份并释放文件句柄（不锁文件），
        /// 全部路径不可用时返回 null。
        /// </summary>
        public static Bitmap TryCreateBitmapFromPaths(IEnumerable<string> paths)
        {
            if (paths == null) return null;

            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) continue;

                try
                {
                    // GDI+ 要求从流构造的 Bitmap 在生命周期内保持流打开，这里复制一份后立即释放句柄
                    using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                    using (var loaded = new Bitmap(fs))
                    {
                        return new Bitmap(loaded);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Failed to load Bitmap from file: {0}", path);
                }
            }

            return null;
        }

        /// <summary>
        /// 运行时段取图：优先按文件路径读图（子线程安全），文件路径全部不可用才回退 WPF 图像转换；
        /// 两条路都失败时抛 "Image Convert Error."。
        /// </summary>
        public static Bitmap CreateRuntimeBitmap(ImageSource imageSource, IEnumerable<string> filePaths)
        {
            return TryCreateBitmapFromPaths(filePaths)
                ?? ConvertImageSourceToBitmap(imageSource)
                ?? throw new Exception("Image Convert Error.");
        }

        /// <summary>
        /// 将 WPF ImageSource 转换为 System.Drawing.Bitmap
        /// </summary>
        public static Bitmap? ConvertImageSourceToBitmap(ImageSource imageSource)
        {
            if (imageSource == null) return null;

            // 未冻结的图像在子线程访问会抛“调用线程无法访问此对象”，这里先尽力冻结（失败不中断转换）
            FreezeIfPossible(imageSource);

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

        public static string IncrementString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "1";

            // 1. 从末尾提取连续的数字或字母后缀
            int suffixStart = input.Length;
            while (suffixStart > 0 && (char.IsDigit(input[suffixStart - 1]) || char.IsLetter(input[suffixStart - 1])))
            {
                suffixStart--;
            }

            string prefix = input.Substring(0, suffixStart);
            string suffix = input.Substring(suffixStart);

            // 2. 如果后缀为空，直接追加 "1"
            if (string.IsNullOrEmpty(suffix))
                return input + "1";

            // 3. 判断后缀类型并分别处理
            if (char.IsDigit(suffix[0]))
            {
                // 数字后缀：加1后强制保持原始位数，溢出截取末尾
                if (long.TryParse(suffix, out long number))
                {
                    number++;
                    string result = number.ToString();
                    if (result.Length > suffix.Length)
                    {
                        result = result.Substring(result.Length - suffix.Length);
                    }
                    return prefix + result;
                }
                // 解析失败，回退为字母处理
                return IncrementAlpha(suffix, prefix);
            }
            else
            {
                // 字母后缀：固定位数循环递增，Z→A 不进位扩展
                return IncrementAlphaFixed(suffix, prefix);
            }
        }

        private static string IncrementAlpha(string suffix, string prefix)
        {
            char[] chars = suffix.ToCharArray();
            int i = chars.Length - 1;

            while (i >= 0)
            {
                char c = chars[i];

                if (char.IsLower(c))
                {
                    if (c < 'z')
                    {
                        chars[i] = (char)(c + 1);
                        return prefix + new string(chars);
                    }
                    else
                    {
                        chars[i] = 'a';
                        i--;
                    }
                }
                else if (char.IsUpper(c))
                {
                    if (c < 'Z')
                    {
                        chars[i] = (char)(c + 1);
                        return prefix + new string(chars);
                    }
                    else
                    {
                        chars[i] = 'A';
                        i--;
                    }
                }
                else
                {
                    break;
                }
            }

            // 所有位都进位了，在最前面补一位（字母进位不受"位数不变"约束）
            if (i < 0)
            {
                char baseChar = char.IsUpper(suffix[0]) ? 'A' : 'a';
                return prefix + baseChar + new string(chars);
            }

            return prefix + new string(chars);
        }

        private static string IncrementAlphaFixed(string suffix, string prefix)
        {
            char[] chars = suffix.ToCharArray();
            int i = chars.Length - 1;

            // 从最后一位开始向前进位
            while (i >= 0)
            {
                char c = chars[i];

                if (char.IsLower(c))
                {
                    if (c < 'z')
                    {
                        chars[i] = (char)(c + 1);
                        return prefix + new string(chars);
                    }
                    else // 'z' 回绕为 'a'，继续向前进位
                    {
                        chars[i] = 'a';
                        i--;
                    }
                }
                else if (char.IsUpper(c))
                {
                    if (c < 'Z')
                    {
                        chars[i] = (char)(c + 1);
                        return prefix + new string(chars);
                    }
                    else // 'Z' 回绕为 'A'，继续向前进位
                    {
                        chars[i] = 'A';
                        i--;
                    }
                }
                else
                {
                    // 非字母字符，停止进位
                    break;
                }
            }

            // 所有位都回绕了（如 "ZZ" → "AA"），位数不变
            // 直接返回回绕后的结果，不再补位
            return prefix + new string(chars);
        }

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
