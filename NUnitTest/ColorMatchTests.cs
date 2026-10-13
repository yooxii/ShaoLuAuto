using NUnit.Framework;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using ShaoLu.Utils;
using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace NUnitTest
{
    /// <summary>
    /// Autogui 颜色（高精度）匹配的单元测试：全部使用合成图像，不依赖真实屏幕。
    /// 匹配核心 <see cref="Autogui.MatchImageColor"/> 是纯函数（输入两张 Bitmap），
    /// FindImageOnScreen 只是它的截屏 + 轮询外壳。
    /// </summary>
    [TestFixture]
    public class ColorMatchTests
    {
        private const int TemplateWidth = 40;
        private const int TemplateHeight = 30;
        private const int SceneWidth = 200;
        private const int SceneHeight = 150;
        private const int TemplateLeft = 100;
        private const int TemplateTop = 60;

        /// <summary>
        /// 与 Autogui.FindImageOnScreen 的 colorThreshold 默认值保持一致。
        /// 实测依据（本文件用例的合成图，Debug|x64）：
        ///   同色 1.000000；同色 + ±2 噪声 0.998864；同色 + ±5 噪声 0.997397；
        ///   红底白字 vs 绿底白字（同形状异色相）0.784288；红底白字 vs 蓝底白字 0.831900；
        ///   红底 (200,30,30) vs (215,45,45) 轻微色偏 0.984453；vs (255,0,0) 0.936841。
        /// 0.95 落在“同色/噪声 ≥ 0.997”与“异色相 ≤ 0.832”之间的空白带，并与需求中
        /// “轻微抗锯齿/压缩噪声应 ≥ 0.95”一致。
        /// </summary>
        private const double DefaultColorThreshold = 0.95;

        /// <summary> 红色模板底与绿色模板底——同形状、不同色相 </summary>
        private static readonly Color RedBackground = Color.FromArgb(200, 30, 30);
        private static readonly Color GreenBackground = Color.FromArgb(30, 200, 30);

        #region 合成图像

        /// <summary>
        /// 合成模板：纯色底 + 三个白色矩形块（硬边缘，灰度上只有两个层级，结构匹配稳定）
        /// </summary>
        private static Bitmap CreateTemplate(Color background)
        {
            Bitmap bmp = new Bitmap(TemplateWidth, TemplateHeight, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(background);
                using (Brush brush = new SolidBrush(Color.White))
                {
                    g.FillRectangle(brush, 4, 4, 12, 8);
                    g.FillRectangle(brush, 20, 10, 8, 16);
                    g.FillRectangle(brush, 6, 20, 18, 6);
                }
            }
            return bmp;
        }

        /// <summary>
        /// 合成目标图：异色背景 + 上下干扰条 + 在指定偏移处原样贴上模板，可选叠加确定性噪声
        /// </summary>
        private static Bitmap CreateScene(Bitmap template, int offsetX, int offsetY, int noiseAmplitude)
        {
            Bitmap bmp = new Bitmap(SceneWidth, SceneHeight, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(20, 40, 80));
                using (Brush brush = new SolidBrush(Color.FromArgb(190, 170, 30)))
                {
                    g.FillRectangle(brush, 0, 0, SceneWidth, 18);
                    g.FillRectangle(brush, 0, SceneHeight - 18, SceneWidth, 18);
                }
                g.DrawImageUnscaled(template, TemplateLeft + offsetX, TemplateTop + offsetY);
            }

            if (noiseAmplitude > 0)
            {
                AddNoise(bmp, noiseAmplitude);
            }
            return bmp;
        }

        private static Bitmap CreateScene(Bitmap template, int offsetX, int offsetY)
        {
            return CreateScene(template, offsetX, offsetY, 0);
        }

        /// <summary> 叠加确定性噪声（固定种子，保证用例可复现） </summary>
        private static void AddNoise(Bitmap bmp, int amplitude)
        {
            Random random = new Random(20240517);
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    int n = random.Next(-amplitude, amplitude + 1);
                    bmp.SetPixel(x, y, Color.FromArgb(Clamp(c.R + n), Clamp(c.G + n), Clamp(c.B + n)));
                }
            }
        }

        private static int Clamp(int value)
        {
            return value < 0 ? 0 : value > 255 ? 255 : value;
        }

        /// <summary>
        /// 旧灰度路径的等价复刻（Autogui.FindImageOnScreen 在 highPrecision=false 时执行的正是这一调用序列）：
        /// BitmapConverter.ToMat → CvtColor(BGR2GRAY) → MatchTemplate(CCoeffNormed) → MinMaxLoc.MaxVal
        /// </summary>
        private static double LegacyGrayMaxSimilarity(Bitmap template, Bitmap scene, out System.Drawing.Point leftTop)
        {
            using (Mat templateMat = BitmapConverter.ToMat(template))
            using (Mat sceneMat = BitmapConverter.ToMat(scene))
            using (Mat grayTemplate = templateMat.CvtColor(ColorConversionCodes.BGR2GRAY))
            using (Mat grayScene = sceneMat.CvtColor(ColorConversionCodes.BGR2GRAY))
            using (Mat result = new Mat())
            {
                Cv2.MatchTemplate(grayScene, grayTemplate, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);
                leftTop = new System.Drawing.Point(maxLoc.X, maxLoc.Y);
                return maxVal;
            }
        }

        #endregion

        #region a) 同图同色（平移 0~2 像素 + 轻微噪声）在颜色模式下命中

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void SameColorWithShiftAndNoise_ColorModeMatches(int shift)
        {
            using (Bitmap template = CreateTemplate(RedBackground))
            using (Bitmap scene = CreateScene(template, shift, shift, 2))
            {
                ColorMatchResult match = Autogui.MatchImageColor(template, scene, 0.8, DefaultColorThreshold);

                Assert.That(match.StructureSimilarity, Is.GreaterThanOrEqualTo(0.8),
                    "结构相似度应达标，实际 " + match.StructureSimilarity.ToString("F4"));
                Assert.That(match.ColorSimilarity, Is.GreaterThanOrEqualTo(0.95),
                    "颜色相似度应 ≥ 0.95，实际 " + match.ColorSimilarity.ToString("F4"));
                Assert.That(match.IsMatch, Is.True,
                    "颜色模式应命中（结构 " + match.StructureSimilarity.ToString("F4") + " / 颜色 " + match.ColorSimilarity.ToString("F4") + "）");
            }
        }

        #endregion

        #region b) 同形状不同颜色：灰度命中、颜色模式不命中

        [Test]
        public void SameShapeDifferentColor_GrayMatchesButColorModeDoesNot()
        {
            using (Bitmap redTemplate = CreateTemplate(RedBackground))
            using (Bitmap greenTemplate = CreateTemplate(GreenBackground))
            using (Bitmap scene = CreateScene(greenTemplate, 0, 0))
            {
                // 场景里贴的是绿底白字的同形状图，模板却来自红底白字
                System.Drawing.Point legacyLoc;
                double legacyGray = LegacyGrayMaxSimilarity(redTemplate, scene, out legacyLoc);
                Assert.That(legacyGray, Is.GreaterThanOrEqualTo(0.8),
                    "灰度模式（threshold=0.8）应命中，实际 " + legacyGray.ToString("F4"));

                ColorMatchResult match = Autogui.MatchImageColor(redTemplate, scene, 0.8, DefaultColorThreshold);
                Assert.That(match.StructureSimilarity, Is.EqualTo(legacyGray).Within(1e-9));
                // 实测 0.784288：色相差异必须让颜色项明显低于任何合理阈值（而结构项仍是 1.0）
                Assert.That(match.ColorSimilarity, Is.LessThan(0.85),
                    "异色不应通过颜色阈值，实际 " + match.ColorSimilarity.ToString("F4"));
                Assert.That(match.ColorSimilarity, Is.LessThan(DefaultColorThreshold));
                Assert.That(match.IsMatch, Is.False,
                    "同一对图两种模式结论必须相反（结构 " + match.StructureSimilarity.ToString("F4") + " / 颜色 " + match.ColorSimilarity.ToString("F4") + "）");
            }
        }

        [Test]
        public void SameShapeSameColor_ColorModeStillMatches()
        {
            // 对照组：同色场景在同样的代码路径下必须命中，排除“颜色模式永远不命中”的实现
            using (Bitmap redTemplate = CreateTemplate(RedBackground))
            using (Bitmap scene = CreateScene(redTemplate, 0, 0))
            {
                ColorMatchResult match = Autogui.MatchImageColor(redTemplate, scene, 0.8, DefaultColorThreshold);

                Assert.That(match.IsMatch, Is.True,
                    "同色场景应命中（结构 " + match.StructureSimilarity.ToString("F4") + " / 颜色 " + match.ColorSimilarity.ToString("F4") + "）");
            }
        }

        #endregion

        #region c) 回归：结构相似度与旧灰度实现一致

        [Test]
        public void GrayRegression_StructureSimilarityEqualsLegacyValue()
        {
            using (Bitmap template = CreateTemplate(RedBackground))
            using (Bitmap scene = CreateScene(template, 2, 1))
            {
                System.Drawing.Point legacyLoc;
                double legacyGray = LegacyGrayMaxSimilarity(template, scene, out legacyLoc);
                ColorMatchResult match = Autogui.MatchImageColor(template, scene, 0.8, DefaultColorThreshold);

                Assert.That(match.StructureSimilarity, Is.EqualTo(legacyGray).Within(1e-12),
                    "结构项必须与旧灰度实现的 maxVal 完全一致");
                // 同图同色且无噪时，CCoeffNormed 在真实位置取 1.0
                Assert.That(match.StructureSimilarity, Is.EqualTo(1.0).Within(1e-9));
                Assert.That(match.StructureSimilarity >= 0.8, Is.True, "旧灰度模式（highPrecision=false）在 0.8 阈值下命中");
                Assert.That(match.LeftTop.X, Is.EqualTo(legacyLoc.X), "结构定位位置应与旧实现一致");
                Assert.That(match.LeftTop.Y, Is.EqualTo(legacyLoc.Y));
            }
        }

        [Test]
        public void GrayRegression_NoisyScene_SimilarityStaysInExpectedRange()
        {
            using (Bitmap template = CreateTemplate(RedBackground))
            using (Bitmap scene = CreateScene(template, 0, 0, 2))
            {
                System.Drawing.Point legacyLoc;
                double legacyGray = LegacyGrayMaxSimilarity(template, scene, out legacyLoc);
                ColorMatchResult match = Autogui.MatchImageColor(template, scene, 0.8, DefaultColorThreshold);

                Assert.That(match.StructureSimilarity, Is.EqualTo(legacyGray).Within(1e-12));
                // ±2 噪声下实测结构相似度 0.999863；用宽松区间断言，避免脆弱绑定到 OpenCV 版本
                Assert.That(match.StructureSimilarity, Is.InRange(0.98, 1.0),
                    "±2 噪声下的结构相似度应仍在 0.98~1.0，实际 " + match.StructureSimilarity.ToString("F6"));
            }
        }

        #endregion

        #region d) 命中位置正确

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void HitPosition_EqualsTemplateLocationInScene(int shift)
        {
            using (Bitmap template = CreateTemplate(RedBackground))
            using (Bitmap scene = CreateScene(template, shift, shift))
            {
                ColorMatchResult match = Autogui.MatchImageColor(template, scene, 0.8, DefaultColorThreshold);

                Assert.That(match.IsMatch, Is.True);
                Assert.That(match.LeftTop.X, Is.EqualTo(TemplateLeft + shift), "LeftTop.X 应与合成图中的模板位置一致");
                Assert.That(match.LeftTop.Y, Is.EqualTo(TemplateTop + shift), "LeftTop.Y 应与合成图中的模板位置一致");

                // FindImageOnScreen 用 maxLoc + 模板尺寸/2 得到 Center，这里用同一换算核对中心点
                int centerX = match.LeftTop.X + TemplateWidth / 2;
                int centerY = match.LeftTop.Y + TemplateHeight / 2;
                Assert.That(centerX, Is.EqualTo(TemplateLeft + shift + TemplateWidth / 2));
                Assert.That(centerY, Is.EqualTo(TemplateTop + shift + TemplateHeight / 2));
            }
        }

        #endregion

        #region 边界

        [Test]
        public void TemplateLargerThanScene_DoesNotMatchAndDoesNotThrow()
        {
            using (Bitmap template = CreateTemplate(RedBackground))
            using (Bitmap tinyScene = new Bitmap(20, 10, PixelFormat.Format32bppArgb))
            {
                ColorMatchResult match = Autogui.MatchImageColor(template, tinyScene, 0.8, DefaultColorThreshold);

                Assert.That(match.IsMatch, Is.False);
                Assert.That(match.ColorSimilarity, Is.EqualTo(0));
            }
        }

        [Test]
        public void NullInputs_DoNotThrow()
        {
            using (Bitmap template = CreateTemplate(RedBackground))
            {
                Assert.That(Autogui.MatchImageColor(null, template).IsMatch, Is.False);
                Assert.That(Autogui.MatchImageColor(template, null).IsMatch, Is.False);
            }
        }

        #endregion
    }
}
