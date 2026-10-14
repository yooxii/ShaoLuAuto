using NUnit.Framework;
using ShaoLu.Converters;
using ShaoLu.Models;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NUnitTest
{
    /// <summary>
    /// 屏幕相对坐标（百分比）换算、相关步骤新字段的兼容性与往返测试
    /// </summary>
    [TestFixture]
    public class ScreenCoordinatesTests
    {
        private string _testDir;

        private static double ScreenW => ScreenCoordinates.ScreenBounds.Width;
        private static double ScreenH => ScreenCoordinates.ScreenBounds.Height;

        [SetUp]
        public void Setup()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "ShaoLu_ScreenCoords_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }

        private string SaveJson(string fileName, ObservableCollection<AutomationStepBase> steps)
        {
            string path = Path.Combine(_testDir, fileName);
#pragma warning disable CS0618 // 旧版序列化 API 用于验证 steps.json 兼容性
            StepsFile.SaveStepsToJson(steps, path);
#pragma warning restore CS0618
            return path;
        }

        private static ObservableCollection<AutomationStepBase> LoadJson(string path)
        {
#pragma warning disable CS0618
            return StepsFile.LoadStepsFromJson(path);
#pragma warning restore CS0618
        }

        /// <summary>显式以 TestDelegate 调用 Assert.Multiple，避开 NUnit 4.6 的重载二义性</summary>
        private static void AssertMultiple(TestDelegate assertions) => Assert.Multiple(assertions);

        /// <summary>删除指定属性所在行，模拟"不含新字段"的旧版 steps.json</summary>
        private static void RemovePropertyLines(string path, params string[] propertyNames)
        {
            string[] lines = File.ReadAllLines(path);
            File.WriteAllLines(path, lines.Where(l => !propertyNames.Any(n => l.Contains("\"" + n + "\""))).ToArray());
        }

        #region 百分比 ↔ 像素换算

        [Test]
        public void PercentToPixel_ExplicitScreenSize_ZeroFiftyHundred()
        {
            AssertMultiple(() =>
            {
                Assert.That(ScreenCoordinates.PercentToPixel(0, 1920), Is.EqualTo(0));
                Assert.That(ScreenCoordinates.PercentToPixel(50, 1920), Is.EqualTo(960));
                Assert.That(ScreenCoordinates.PercentToPixel(100, 1920), Is.EqualTo(1920));
                Assert.That(ScreenCoordinates.PercentToPixel(0, 1080), Is.EqualTo(0));
                Assert.That(ScreenCoordinates.PercentToPixel(50, 1080), Is.EqualTo(540));
                Assert.That(ScreenCoordinates.PercentToPixel(100, 1080), Is.EqualTo(1080));
                Assert.That(ScreenCoordinates.PercentToPixel(12.5, 1920), Is.EqualTo(240));
            });
        }

        [Test]
        public void PixelToPercent_ExplicitScreenSize_ZeroFiftyHundred()
        {
            AssertMultiple(() =>
            {
                Assert.That(ScreenCoordinates.PixelToPercent(0, 1920), Is.EqualTo(0));
                Assert.That(ScreenCoordinates.PixelToPercent(960, 1920), Is.EqualTo(50));
                Assert.That(ScreenCoordinates.PixelToPercent(1920, 1920), Is.EqualTo(100));
                Assert.That(ScreenCoordinates.PixelToPercent(0, 1080), Is.EqualTo(0));
                Assert.That(ScreenCoordinates.PixelToPercent(540, 1080), Is.EqualTo(50));
                Assert.That(ScreenCoordinates.PixelToPercent(1080, 1080), Is.EqualTo(100));
            });
        }

        [Test]
        public void PercentPixel_OutOfRange_LinesarlyExtrapolatesWithoutThrow()
        {
            AssertMultiple(() =>
            {
                Assert.That(ScreenCoordinates.PercentToPixel(-10, 1920), Is.EqualTo(-192));
                Assert.That(ScreenCoordinates.PercentToPixel(150, 1920), Is.EqualTo(2880));
                Assert.That(ScreenCoordinates.PixelToPercent(-96, 1920), Is.EqualTo(-5));
                Assert.That(ScreenCoordinates.PixelToPercent(2880, 1920), Is.EqualTo(150));
            });
        }

        [Test]
        public void PercentPixel_ZeroOrNegativeScreenSize_ReturnsZeroWithoutThrow()
        {
            AssertMultiple(() =>
            {
                Assert.That(ScreenCoordinates.PercentToPixel(50, 0), Is.EqualTo(0));
                Assert.That(ScreenCoordinates.PercentToPixel(50, -1920), Is.EqualTo(0));
                Assert.That(ScreenCoordinates.PercentToPixel(double.NaN, 0), Is.EqualTo(0));
                Assert.That(ScreenCoordinates.PixelToPercent(960, 0), Is.EqualTo(0));
                Assert.That(ScreenCoordinates.PixelToPercent(960, -1920), Is.EqualTo(0));
            });
        }

        [Test]
        public void ScreenBounds_IsReadableWithoutThrow()
        {
            var bounds = ScreenCoordinates.ScreenBounds;
            AssertMultiple(() =>
            {
                Assert.That(bounds.Width, Is.GreaterThanOrEqualTo(0));
                Assert.That(bounds.Height, Is.GreaterThanOrEqualTo(0));
            });
        }

        [Test]
        public void ToPixel_AbsoluteMode_ReturnsValueUnchanged()
        {
            AssertMultiple(() =>
            {
                Assert.That(ScreenCoordinates.ToPixelX(123.5, false), Is.EqualTo(123.5));
                Assert.That(ScreenCoordinates.ToPixelY(-7, false), Is.EqualTo(-7));
                Assert.That(ScreenCoordinates.ToPixelWidth(800, false), Is.EqualTo(800));
                Assert.That(ScreenCoordinates.ToPixelHeight(0.25, false), Is.EqualTo(0.25));
            });
        }

        [Test]
        public void ToPixel_RelativeMode_UsesPrimaryScreenBounds()
        {
            Assume.That(ScreenW, Is.GreaterThan(0), "需要可用的主屏宽度");
            Assume.That(ScreenH, Is.GreaterThan(0), "需要可用的主屏高度");
            AssertMultiple(() =>
            {
                Assert.That(ScreenCoordinates.ToPixelX(0, true), Is.EqualTo(0));
                Assert.That(ScreenCoordinates.ToPixelX(100, true), Is.EqualTo(ScreenW));
                Assert.That(ScreenCoordinates.ToPixelY(100, true), Is.EqualTo(ScreenH));
                Assert.That(ScreenCoordinates.ToPixelX(50, true), Is.EqualTo(ScreenW * 0.5).Within(1e-9));
                Assert.That(ScreenCoordinates.ToPixelY(25, true), Is.EqualTo(ScreenH * 0.25).Within(1e-9));
                Assert.That(ScreenCoordinates.ToPixelWidth(50, true), Is.EqualTo(ScreenW * 0.5).Within(1e-9));
                Assert.That(ScreenCoordinates.ToPixelHeight(25, true), Is.EqualTo(ScreenH * 0.25).Within(1e-9));
            });
        }

        [Test]
        public void ToPercent_PixelRoundTrip_ReturnsOriginalPercent()
        {
            Assume.That(ScreenW, Is.GreaterThan(0), "需要可用的主屏宽度");
            Assume.That(ScreenH, Is.GreaterThan(0), "需要可用的主屏高度");
            AssertMultiple(() =>
            {
                Assert.That(ScreenCoordinates.ToPercentX(ScreenCoordinates.ToPixelX(37.5, true)), Is.EqualTo(37.5).Within(1e-9));
                Assert.That(ScreenCoordinates.ToPercentY(ScreenCoordinates.ToPixelY(12.5, true)), Is.EqualTo(12.5).Within(1e-9));
                Assert.That(ScreenCoordinates.ToPercentWidth(ScreenCoordinates.ToPixelWidth(80, true)), Is.EqualTo(80).Within(1e-9));
                Assert.That(ScreenCoordinates.ToPercentHeight(ScreenCoordinates.ToPixelHeight(60, true)), Is.EqualTo(60).Within(1e-9));
            });
        }

        [Test]
        public void Convert_SameUnit_ReturnsValueUnchanged()
        {
            AssertMultiple(() =>
            {
                Assert.That(ScreenCoordinates.ConvertX(960, false, false), Is.EqualTo(960));
                Assert.That(ScreenCoordinates.ConvertY(50, true, true), Is.EqualTo(50));
                Assert.That(ScreenCoordinates.ConvertWidth(300, false, false), Is.EqualTo(300));
                Assert.That(ScreenCoordinates.ConvertHeight(400, true, true), Is.EqualTo(400));
            });
        }

        [Test]
        public void Convert_PixelToPercentAndBack_PreservesPixel()
        {
            Assume.That(ScreenW, Is.GreaterThan(0), "需要可用的主屏宽度");
            Assume.That(ScreenH, Is.GreaterThan(0), "需要可用的主屏高度");
            AssertMultiple(() =>
            {
                Assert.That(ScreenCoordinates.ConvertX(960, false, true), Is.EqualTo(ScreenCoordinates.ToPercentX(960)));
                Assert.That(ScreenCoordinates.ConvertX(ScreenCoordinates.ConvertX(960, false, true), true, false), Is.EqualTo(960).Within(1e-6));
                Assert.That(ScreenCoordinates.ConvertY(540, false, true), Is.EqualTo(ScreenCoordinates.ToPercentY(540)));
                Assert.That(ScreenCoordinates.ConvertY(ScreenCoordinates.ConvertY(540, false, true), true, false), Is.EqualTo(540).Within(1e-6));
            });
        }

        #endregion

        #region 枚举数值与下拉项（向后兼容）

        [Test]
        public void Enums_NumericValues_AreStableForOldFiles()
        {
            AssertMultiple(() =>
            {
                Assert.That((int)PositionMode.Absolute, Is.EqualTo(0));
                Assert.That((int)PositionMode.CurrentMouse, Is.EqualTo(1));
                Assert.That((int)PositionMode.Relative, Is.EqualTo(2));
                Assert.That((int)TextPointSource.Absolute, Is.EqualTo(0));
                Assert.That((int)TextPointSource.Relative, Is.EqualTo(1));
                Assert.That((int)TextPointSource.ScreenRelative, Is.EqualTo(2));
            });
        }

        [Test]
        public void DropdownLists_ContainNewOptions()
        {
            AssertMultiple(() =>
            {
                Assert.That(new MouseActionStep().PositionModes, Does.Contain(PositionMode.Relative));
                Assert.That(new TextPointItem().Sources, Does.Contain(TextPointSource.ScreenRelative));
                Assert.That(new TextPointItem().Sources, Does.Contain(TextPointSource.Relative));
            });
        }

        #endregion

        #region 旧版 steps.json 兼容（不含新字段）

        [Test]
        public void LoadSteps_OldFileWithoutNewFields_UsesAbsoluteDefaults()
        {
            var steps = new ObservableCollection<AutomationStepBase>
            {
                new PopupStep("弹窗") { WindowX = 100, WindowY = 200 },
                new MouseActionStep("鼠标") { PositionMode = PositionMode.CurrentMouse, TargetX = 10, TargetY = 20 },
                new StatisticsStep("统计") { WindowX = 300, WindowY = 400 },
            };
            string path = SaveJson("old_steps.json", steps);
            string before = File.ReadAllText(path);
            Assert.That(before, Does.Contain("\"WindowPositionRelative\""));

            RemovePropertyLines(path, "WindowPositionRelative");
            Assert.That(File.ReadAllText(path), Does.Not.Contain("WindowPositionRelative"));

            var loaded = LoadJson(path);

            var popup = loaded[0] as PopupStep;
            Assert.That(popup, Is.Not.Null);
            Assert.That(popup.WindowPositionRelative, Is.False);
            Assert.That(popup.WindowX, Is.EqualTo(100));
            Assert.That(popup.WindowY, Is.EqualTo(200));

            var mouse = loaded[1] as MouseActionStep;
            Assert.That(mouse, Is.Not.Null);
            Assert.That(mouse.PositionMode, Is.EqualTo(PositionMode.CurrentMouse));
            Assert.That((int)mouse.PositionMode, Is.EqualTo(1));
            Assert.That(mouse.TargetX, Is.EqualTo(10));
            Assert.That(mouse.TargetY, Is.EqualTo(20));

            var stats = loaded[2] as StatisticsStep;
            Assert.That(stats, Is.Not.Null);
            Assert.That(stats.WindowPositionRelative, Is.False);
            Assert.That(stats.WindowX, Is.EqualTo(300));
            Assert.That(stats.WindowY, Is.EqualTo(400));
        }

        [Test]
        public void BurnInConfig_OldJsonWithoutRegionRelative_DefaultsToFalse()
        {
            const string json = "{\"RegionX\":100,\"RegionY\":200,\"RegionW\":300,\"RegionH\":400,\"CaptureScreenshot\":true}";
            var config = JsonSerializer.Deserialize<BurnInConfig>(json);
            AssertMultiple(() =>
            {
                Assert.That(config.RegionRelative, Is.False);
                Assert.That(config.RegionX, Is.EqualTo(100));
                Assert.That(config.RegionY, Is.EqualTo(200));
                Assert.That(config.RegionW, Is.EqualTo(300));
                Assert.That(config.RegionH, Is.EqualTo(400));
            });
        }

        [Test]
        public void MouseActionItem_OldJsonDragPath_LoadsIntegerPoints()
        {
            const string json = "{\"ActionType\":6,\"Count\":1,\"Interval\":0.1," +
                                "\"DragPath\":[{\"X\":100,\"Y\":200,\"IsEmpty\":false},{\"X\":150,\"Y\":250,\"IsEmpty\":false}]," +
                                "\"ClickDragTrajectories\":[]}";
            var item = JsonSerializer.Deserialize<MouseActionItem>(json);
            AssertMultiple(() =>
            {
                Assert.That(item.DragPath.Count, Is.EqualTo(2));
                Assert.That(item.DragPath[0].X, Is.EqualTo(100));
                Assert.That(item.DragPath[0].Y, Is.EqualTo(200));
                Assert.That(item.DragPath[1].X, Is.EqualTo(150));
                Assert.That(item.DragPath[1].Y, Is.EqualTo(250));
            });
        }

        [Test]
        public void DragPath_AcceptsLegacyIntegerPointAndAllowsInPlaceEdit()
        {
            var item = new MouseActionItem();
            item.DragPath.Add(new Point(100, 200));
            Assert.That(item.DragPath[0].X, Is.EqualTo(100));
            Assert.That(item.DragPath[0].Y, Is.EqualTo(200));

            item.DragPath[0].X = 999;
            Assert.That(item.DragPath[0].X, Is.EqualTo(999));
        }

        #endregion

        #region 相对模式往返（存百分比）

        [Test]
        public void SaveAndLoad_MouseActionStep_RelativePercent_RoundTrip()
        {
            var step = new MouseActionStep("鼠标相对")
            {
                PositionMode = PositionMode.Relative,
                TargetX = 37.5,
                TargetY = 62.5,
            };
            step.MouseActions.Clear();
            var action = new MouseActionItem { ActionType = MouseActionType.Drag };
            action.DragPath.Add(new DragPathPoint(10, 20));
            action.DragPath.Add(new DragPathPoint(80.5, 90.25));
            step.MouseActions.Add(action);

            string path = SaveJson("mouse_relative.json", new ObservableCollection<AutomationStepBase> { step });
            string json = File.ReadAllText(path);
            AssertMultiple(() =>
            {
                Assert.That(json, Does.Contain("\"TargetX\": 37.5"));
                Assert.That(json, Does.Contain("\"TargetY\": 62.5"));
                Assert.That(json, Does.Contain("80.5"));
            });

            var loaded = LoadJson(path)[0] as MouseActionStep;
            Assert.That(loaded, Is.Not.Null);
            AssertMultiple(() =>
            {
                Assert.That(loaded.PositionMode, Is.EqualTo(PositionMode.Relative));
                Assert.That((int)loaded.PositionMode, Is.EqualTo(2));
                Assert.That(loaded.TargetX, Is.EqualTo(37.5));
                Assert.That(loaded.TargetY, Is.EqualTo(62.5));
                Assert.That(loaded.MouseActions.Count, Is.EqualTo(1));
                Assert.That(loaded.MouseActions[0].DragPath.Count, Is.EqualTo(2));
                Assert.That(loaded.MouseActions[0].DragPath[0].X, Is.EqualTo(10));
                Assert.That(loaded.MouseActions[0].DragPath[0].Y, Is.EqualTo(20));
                Assert.That(loaded.MouseActions[0].DragPath[1].X, Is.EqualTo(80.5));
                Assert.That(loaded.MouseActions[0].DragPath[1].Y, Is.EqualTo(90.25));
            });
        }

        [Test]
        public void SaveAndLoad_GetInputStep_ScreenRelativeTextPoint_RoundTrip()
        {
            var step = new GetInputStep("获取输入") { InputMode = GetInputMode.ScreenText };
            step.TextPoints.Clear();
            step.TextPoints.Add(new TextPointItem { Source = TextPointSource.ScreenRelative, X = 12.5, Y = 87.5 });

            string path = SaveJson("getinput_relative.json", new ObservableCollection<AutomationStepBase> { step });
            string json = File.ReadAllText(path);
            Assert.That(json, Does.Contain("\"X\": 12.5"));

            var loaded = LoadJson(path)[0] as GetInputStep;
            Assert.That(loaded, Is.Not.Null);
            AssertMultiple(() =>
            {
                Assert.That(loaded.TextPoints.Count, Is.EqualTo(1));
                Assert.That(loaded.TextPoints[0].Source, Is.EqualTo(TextPointSource.ScreenRelative));
                Assert.That((int)loaded.TextPoints[0].Source, Is.EqualTo(2));
                Assert.That(loaded.TextPoints[0].X, Is.EqualTo(12.5));
                Assert.That(loaded.TextPoints[0].Y, Is.EqualTo(87.5));
            });
        }

        [Test]
        public void BurnInConfig_RelativePercent_RoundTrip()
        {
            var config = new BurnInConfig
            {
                RegionRelative = true,
                RegionX = 10,
                RegionY = 20,
                RegionW = 30,
                RegionH = 40,
            };

            string json = JsonSerializer.Serialize(config);
            Assert.That(json, Does.Contain("\"RegionRelative\":true"));

            var loaded = JsonSerializer.Deserialize<BurnInConfig>(json);
            AssertMultiple(() =>
            {
                Assert.That(loaded.RegionRelative, Is.True);
                Assert.That(loaded.RegionX, Is.EqualTo(10));
                Assert.That(loaded.RegionY, Is.EqualTo(20));
                Assert.That(loaded.RegionW, Is.EqualTo(30));
                Assert.That(loaded.RegionH, Is.EqualTo(40));
            });
        }

        #endregion

        #region Clone 复制新字段

        [Test]
        public void MouseActionStep_Clone_CopiesPositionModeAndRelativeCoordinates()
        {
            var step = new MouseActionStep("s")
            {
                PositionMode = PositionMode.Relative,
                TargetX = 25,
                TargetY = 75,
            };
            var clone = step.Clone() as MouseActionStep;
            Assert.That(clone, Is.Not.Null);
            AssertMultiple(() =>
            {
                Assert.That(clone.PositionMode, Is.EqualTo(PositionMode.Relative));
                Assert.That(clone.TargetX, Is.EqualTo(25));
                Assert.That(clone.TargetY, Is.EqualTo(75));
            });
        }

        [Test]
        public void PopupStep_Clone_CopiesWindowPositionRelative()
        {
            var step = new PopupStep("p") { WindowPositionRelative = true };
            step.WindowX = 25;
            step.WindowY = 40;

            var clone = step.Clone() as PopupStep;
            Assert.That(clone, Is.Not.Null);
            AssertMultiple(() =>
            {
                Assert.That(clone.WindowPositionRelative, Is.True);
                Assert.That(clone.WindowX, Is.EqualTo(25));
                Assert.That(clone.WindowY, Is.EqualTo(40));
                Assert.That(step.WindowX, Is.EqualTo(25));
            });
        }

        [Test]
        public void StatisticsStep_Clone_CopiesWindowPositionRelative()
        {
            var step = new StatisticsStep("t") { WindowPositionRelative = true };
            step.WindowX = 12.5;
            step.WindowY = 62.5;

            var clone = step.Clone() as StatisticsStep;
            Assert.That(clone, Is.Not.Null);
            AssertMultiple(() =>
            {
                Assert.That(clone.WindowPositionRelative, Is.True);
                Assert.That(clone.WindowX, Is.EqualTo(12.5));
                Assert.That(clone.WindowY, Is.EqualTo(62.5));
            });
        }

        [Test]
        public void BurnInConfigStep_Clone_CopiesRegionRelative()
        {
            var step = new BurnInConfigStep("b");
            step.Config.RegionRelative = true;
            step.Config.RegionX = 10;
            step.Config.RegionY = 20;
            step.Config.RegionW = 30;
            step.Config.RegionH = 40;

            var clone = step.Clone() as BurnInConfigStep;
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone.Config, Is.Not.SameAs(step.Config));
            AssertMultiple(() =>
            {
                Assert.That(clone.Config.RegionRelative, Is.True);
                Assert.That(clone.Config.RegionX, Is.EqualTo(10));
                Assert.That(clone.Config.RegionY, Is.EqualTo(20));
                Assert.That(clone.Config.RegionW, Is.EqualTo(30));
                Assert.That(clone.Config.RegionH, Is.EqualTo(40));
            });
        }

        #endregion

        #region 拖拽路径与模式切换换算

        [Test]
        public void ResolveDragPathPixels_RelativeMode_ConvertsPercentToPhysicalPixels()
        {
            Assume.That(ScreenW, Is.GreaterThan(0), "需要可用的主屏宽度");
            Assume.That(ScreenH, Is.GreaterThan(0), "需要可用的主屏高度");

            var step = new MouseActionStep("s") { PositionMode = PositionMode.Relative };
            var action = new MouseActionItem();
            action.DragPath.Add(new DragPathPoint(0, 0));
            action.DragPath.Add(new DragPathPoint(50, 25));
            action.DragPath.Add(new DragPathPoint(100, 100));

            var points = step.ResolveDragPathPixels(action, 1.0, 1.0);
            AssertMultiple(() =>
            {
                Assert.That(points.Count, Is.EqualTo(3));
                Assert.That(points[0], Is.EqualTo(new System.Drawing.Point(0, 0)));
                Assert.That(points[1], Is.EqualTo(new System.Drawing.Point((int)(ScreenW * 0.5), (int)(ScreenH * 0.25))));
                Assert.That(points[2], Is.EqualTo(new System.Drawing.Point((int)ScreenW, (int)ScreenH)));
            });
        }

        [Test]
        public void ResolveDragPathPixels_AbsoluteMode_UsesPixelsTimesDpi()
        {
            var step = new MouseActionStep("s");
            var action = new MouseActionItem();
            action.DragPath.Add(new DragPathPoint(100, 200));
            action.DragPath.Add(new DragPathPoint(150.5, 250.25));

            var points = step.ResolveDragPathPixels(action, 1.5, 2.0);
            AssertMultiple(() =>
            {
                Assert.That(points[0], Is.EqualTo(new System.Drawing.Point(150, 400)));
                Assert.That(points[1], Is.EqualTo(new System.Drawing.Point(225, 500)));
            });
        }

        [Test]
        public void MouseActionStep_ToggleRelativeMode_ConvertsCoordinatesAndDragPath()
        {
            Assume.That(ScreenW, Is.GreaterThan(0), "需要可用的主屏宽度");
            Assume.That(ScreenH, Is.GreaterThan(0), "需要可用的主屏高度");

            var step = new MouseActionStep("s") { TargetX = 100, TargetY = 200 };
            var action = new MouseActionItem();
            action.DragPath.Add(new DragPathPoint(100, 200));
            step.MouseActions.Clear();
            step.MouseActions.Add(action);

            step.PositionMode = PositionMode.Relative;
            AssertMultiple(() =>
            {
                Assert.That(step.TargetX, Is.EqualTo(ScreenCoordinates.ToPercentX(100)).Within(1e-9));
                Assert.That(step.TargetY, Is.EqualTo(ScreenCoordinates.ToPercentY(200)).Within(1e-9));
                Assert.That(action.DragPath[0].X, Is.EqualTo(ScreenCoordinates.ToPercentX(100)).Within(1e-9));
                Assert.That(action.DragPath[0].Y, Is.EqualTo(ScreenCoordinates.ToPercentY(200)).Within(1e-9));
            });

            step.PositionMode = PositionMode.Absolute;
            AssertMultiple(() =>
            {
                Assert.That(step.TargetX, Is.EqualTo(100).Within(1e-6));
                Assert.That(step.TargetY, Is.EqualTo(200).Within(1e-6));
                Assert.That(action.DragPath[0].X, Is.EqualTo(100).Within(1e-6));
                Assert.That(action.DragPath[0].Y, Is.EqualTo(200).Within(1e-6));
            });
        }

        [Test]
        public void PopupStep_ToggleRelative_ConvertsPositionAndKeepsCenteredSentinel()
        {
            // -1 为"居中显示"哨兵值，切换模式时不得换算
            var centered = new PopupStep("p");
            centered.WindowPositionRelative = true;
            AssertMultiple(() =>
            {
                Assert.That(centered.WindowX, Is.EqualTo(-1));
                Assert.That(centered.WindowY, Is.EqualTo(-1));
            });

            Assume.That(ScreenW, Is.GreaterThan(0), "需要可用的主屏宽度");
            Assume.That(ScreenH, Is.GreaterThan(0), "需要可用的主屏高度");

            var step = new PopupStep("p") { WindowX = 100, WindowY = 200 };
            step.WindowPositionRelative = true;
            AssertMultiple(() =>
            {
                Assert.That(step.WindowX, Is.EqualTo(ScreenCoordinates.ToPercentX(100)).Within(1e-9));
                Assert.That(step.WindowY, Is.EqualTo(ScreenCoordinates.ToPercentY(200)).Within(1e-9));
            });

            step.WindowPositionRelative = false;
            AssertMultiple(() =>
            {
                Assert.That(step.WindowX, Is.EqualTo(100).Within(1e-6));
                Assert.That(step.WindowY, Is.EqualTo(200).Within(1e-6));
            });
        }

        [Test]
        public void StatisticsStep_ToggleRelative_ConvertsPosition()
        {
            Assume.That(ScreenW, Is.GreaterThan(0), "需要可用的主屏宽度");

            var step = new StatisticsStep("t") { WindowX = 100, WindowY = 200 };
            step.WindowPositionRelative = true;
            Assert.That(step.WindowX, Is.EqualTo(ScreenCoordinates.ToPercentX(100)).Within(1e-9));

            step.WindowPositionRelative = false;
            AssertMultiple(() =>
            {
                Assert.That(step.WindowX, Is.EqualTo(100).Within(1e-6));
                Assert.That(step.WindowY, Is.EqualTo(200).Within(1e-6));
            });
        }

        [Test]
        public void TextPointItem_ScreenRelative_ToggleConvertsAndSummaryShowsPercent()
        {
            var item = new TextPointItem { X = 100, Y = 200 };
            Assert.That(item.Source, Is.EqualTo(TextPointSource.Absolute));
            Assert.That(item.Summary, Does.Not.Contain("%"));

            Assume.That(ScreenW, Is.GreaterThan(0), "需要可用的主屏宽度");
            Assume.That(ScreenH, Is.GreaterThan(0), "需要可用的主屏高度");

            item.Source = TextPointSource.ScreenRelative;
            Assert.That(item.Summary, Does.Contain("%"));
            AssertMultiple(() =>
            {
                Assert.That(item.X, Is.EqualTo(ScreenCoordinates.ToPercentX(100)).Within(1e-9));
                Assert.That(item.Y, Is.EqualTo(ScreenCoordinates.ToPercentY(200)).Within(1e-9));
            });

            // 离开屏幕相对坐标即回到像素口径（图像相对定位不占用 X/Y，但同样按像素保存）
            item.Source = TextPointSource.Relative;
            Assert.That(item.X, Is.EqualTo(100).Within(1e-6));

            item.Source = TextPointSource.Absolute;
            AssertMultiple(() =>
            {
                Assert.That(item.X, Is.EqualTo(100).Within(1e-6));
                Assert.That(item.Y, Is.EqualTo(200).Within(1e-6));
            });
        }

        [Test]
        public void BurnInConfig_ToggleRegionRelative_ConvertsNullableRegion()
        {
            Assume.That(ScreenW, Is.GreaterThan(0), "需要可用的主屏宽度");
            Assume.That(ScreenH, Is.GreaterThan(0), "需要可用的主屏高度");

            var config = new BurnInConfig { RegionX = 100, RegionY = 200, RegionW = 300, RegionH = 400 };
            config.RegionRelative = true;
            AssertMultiple(() =>
            {
                Assert.That(config.RegionX.Value, Is.EqualTo(ScreenCoordinates.ToPercentX(100)).Within(1e-9));
                Assert.That(config.RegionY.Value, Is.EqualTo(ScreenCoordinates.ToPercentY(200)).Within(1e-9));
                Assert.That(config.RegionW.Value, Is.EqualTo(ScreenCoordinates.ToPercentWidth(300)).Within(1e-9));
                Assert.That(config.RegionH.Value, Is.EqualTo(ScreenCoordinates.ToPercentHeight(400)).Within(1e-9));
            });

            config.RegionRelative = false;
            AssertMultiple(() =>
            {
                Assert.That(config.RegionX.Value, Is.EqualTo(100).Within(1e-6));
                Assert.That(config.RegionY.Value, Is.EqualTo(200).Within(1e-6));
                Assert.That(config.RegionW.Value, Is.EqualTo(300).Within(1e-6));
                Assert.That(config.RegionH.Value, Is.EqualTo(400).Within(1e-6));
            });
        }

        [Test]
        public void BurnInConfig_ToggleRegionRelative_WithEmptyRegion_DoesNotThrow()
        {
            var config = new BurnInConfig();
            config.RegionRelative = true;
            AssertMultiple(() =>
            {
                Assert.That(config.RegionX, Is.Null);
                Assert.That(config.RegionW, Is.Null);
                Assert.That(config.HasRegion, Is.False);
            });
        }

        #endregion

        #region 枚举显示标签

        [Test]
        public void Converters_NewEnumMembers_ReturnNonEmptyLabels()
        {
            AssertMultiple(() =>
            {
                Assert.That(ConditionEnumToStringConverter.ConvertPositionMode(PositionMode.Relative), Is.Not.Null.And.Not.Empty);
                Assert.That(ConditionEnumToStringConverter.ConvertPositionMode(PositionMode.Absolute), Is.Not.Null.And.Not.Empty);
                Assert.That(ConditionEnumToStringConverter.ConvertTextPointSource(TextPointSource.ScreenRelative), Is.Not.Null.And.Not.Empty);
                Assert.That(ConditionEnumToStringConverter.ConvertTextPointSource(TextPointSource.Absolute), Is.Not.Null.And.Not.Empty);
            });
        }

        #endregion
    }
}
