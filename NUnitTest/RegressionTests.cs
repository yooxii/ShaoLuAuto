using NUnit.Framework;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace NUnitTest
{
    /// <summary>
    /// 针对代码审查报告的回归测试：
    /// S1 保存原子性、S3 图片落地、S4 烧录模板打包、S5/S6 Clone 完整性、M23 打开损坏包不毁工作目录。
    /// 验收门槛：每个 Clone 的集合数量一致、二次保存包内图片不减、模板另存后仍可匹配。
    /// </summary>
    [TestFixture]
    public class RegressionTests
    {
        private string _testDir;
        private Func<string> _originalWorkDirResolver;

        [SetUp]
        public void SetUp()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "ShaoLu_Regression_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _originalWorkDirResolver = StepsFile.WorkDirResolver;
        }

        [TearDown]
        public void TearDown()
        {
            StepsFile.WorkDirResolver = _originalWorkDirResolver;
            try
            {
                if (Directory.Exists(_testDir))
                    Directory.Delete(_testDir, true);
            }
            catch
            {
                // 测试清理失败不影响结论
            }
        }

        private static readonly byte[] TinyPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        #region S5 / S6 Clone 完整性

        [Test]
        public void StatisticsStep_Clone_KeepsItemCount()
        {
            var step = new StatisticsStep("统计步骤");
            int countBefore = step.StatisticsItems.Count;

            var clone = (StatisticsStep)step.Clone();

            Assert.That(countBefore, Is.GreaterThan(0), "统计步骤默认应带统计项");
            Assert.That(clone.StatisticsItems.Count, Is.EqualTo(countBefore), "Clone 不应叠加默认统计项（5→10）");
        }

        [Test]
        public void StatisticsStep_Clone_SecondGeneration_StillStable()
        {
            var step = new StatisticsStep("统计步骤");
            int countBefore = step.StatisticsItems.Count;

            var clone1 = (StatisticsStep)step.Clone();
            var clone2 = (StatisticsStep)clone1.Clone();

            Assert.That(clone1.StatisticsItems.Count, Is.EqualTo(countBefore));
            Assert.That(clone2.StatisticsItems.Count, Is.EqualTo(countBefore));
            Assert.That(clone2.StatisticsItems, Is.Not.SameAs(clone1.StatisticsItems));
        }

        [Test]
        public void MouseActionStep_Clone_CopiesAllActions()
        {
            var step = new MouseActionStep("鼠标操作");
            step.MouseActions.Add(new ShaoLu.Models.MouseActionItem { ActionType = ShaoLu.Models.MouseActionType.LeftClick, Count = 1 });
            step.MouseActions.Add(new ShaoLu.Models.MouseActionItem { ActionType = ShaoLu.Models.MouseActionType.DoubleClick, Count = 2 });

            var clone = (MouseActionStep)step.Clone();

            Assert.That(step.MouseActions.Count, Is.EqualTo(3), "步骤默认应带一个动作，加上测试新增的两个");
            Assert.That(clone.MouseActions.Count, Is.EqualTo(step.MouseActions.Count), "Clone 必须复制动作序列，而不是只按容量建空列表");
            Assert.That(clone.MouseActions[0], Is.Not.SameAs(step.MouseActions[0]));
            Assert.That(clone.MouseActions[2].Count, Is.EqualTo(2));
        }

        #endregion

        #region S1 保存原子性 / 备份

        [Test]
        public void SaveAsAutoStepPackage_Overwrite_KeepsBackupAndValidPackage()
        {
            var steps = new ObservableCollection<AutomationStepBase> { new TypeTextStep("步骤A") };
            string packagePath = Path.Combine(_testDir, "atomic.autostep");

            StepsFile.SaveAsAutoStepPackage(steps, packagePath);
            var result = StepsFile.SaveAsAutoStepPackage(steps, packagePath);

            Assert.That(File.Exists(packagePath), Is.True);
            Assert.That(result.BackupPath, Is.Not.Null, "覆盖保存应保留上一版 .bak");
            Assert.That(File.Exists(result.BackupPath), Is.True);

            using (var zip = ZipFile.OpenRead(packagePath))
            {
                Assert.That(zip.GetEntry("steps.json"), Is.Not.Null);
            }
        }

        [Test]
        public void SaveAsAutoStepPackage_DoesNotLeaveTempFiles()
        {
            var steps = new ObservableCollection<AutomationStepBase> { new TypeTextStep("步骤A") };
            string packagePath = Path.Combine(_testDir, "clean.autostep");

            StepsFile.SaveAsAutoStepPackage(steps, packagePath);

            var leftovers = Directory.GetFiles(_testDir, "*.tmp-*");
            Assert.That(leftovers, Is.Empty, "保存成功后不应残留临时文件");
        }

        #endregion

        #region S4 烧录判据模板打包

        [Test]
        public void SaveAsAutoStepPackage_PacksBurnInTemplates()
        {
            string workDir = Path.Combine(_testDir, "src_work");
            Directory.CreateDirectory(Path.Combine(workDir, "images"));
            File.WriteAllBytes(Path.Combine(workDir, "images", "burnin_good.png"), TinyPng);
            StepsFile.WorkDirResolver = () => workDir;

            var burnStep = new BurnInConfigStep("烧录判定") { GoodTemplateName = "images/burnin_good.png" };
            var steps = new ObservableCollection<AutomationStepBase> { burnStep };

            string packagePath = Path.Combine(_testDir, "burn.autostep");
            var result = StepsFile.SaveAsAutoStepPackage(steps, packagePath);

            Assert.That(result.MissingImages, Is.Empty);
            using (var zip = ZipFile.OpenRead(packagePath))
            {
                Assert.That(zip.GetEntry("images/burnin_good.png"), Is.Not.Null, "烧录模板必须进包，否则换机后三路命中全 false");
            }
        }

        [Test]
        public void SaveAsAutoStepPackage_MissingTemplate_IsReported()
        {
            var burnStep = new BurnInConfigStep("烧录判定") { GoodTemplateName = "images/not_exists.png" };
            var steps = new ObservableCollection<AutomationStepBase> { burnStep };

            string packagePath = Path.Combine(_testDir, "missing.autostep");
            var result = StepsFile.SaveAsAutoStepPackage(steps, packagePath);

            Assert.That(result.HasMissingImages, Is.True, "缺失模板不能静默跳过");
            Assert.That(result.MissingImages.Any(m => m.Contains("not_exists.png")), Is.True);
        }

        #endregion

        #region S3 保存后图片落地

        [Test]
        public void SaveAsAutoStepPackage_LandsImagesIntoCurrentWorkDir()
        {
            string workDir = Path.Combine(_testDir, "work");
            Directory.CreateDirectory(Path.Combine(workDir, "images"));
            File.WriteAllBytes(Path.Combine(workDir, "images", "burnin_good.png"), TinyPng);
            StepsFile.WorkDirResolver = () => workDir;

            var burnStep = new BurnInConfigStep("烧录判定") { GoodTemplateName = "images/burnin_good.png" };
            var steps = new ObservableCollection<AutomationStepBase> { burnStep };

            string packagePath = Path.Combine(_testDir, "saveas.autostep");
            var result = StepsFile.SaveAsAutoStepPackage(steps, packagePath);

            Assert.That(result.WorkDir, Is.EqualTo(workDir), "保存沿用当前工作目录");
            Assert.That(File.Exists(Path.Combine(workDir, "images", "burnin_good.png")), Is.True,
                "图片必须留在当前工作目录，否则第二次保存会剔除图片");
            Assert.That(Directory.Exists(Path.Combine(_testDir, "saveas_images")), Is.False,
                "不应再在工程文件旁边生成 {包名}_images 目录");

            // 第二次保存（模拟用户再按 Ctrl+S）：包里图片数量不减少
            var second = StepsFile.SaveAsAutoStepPackage(steps, packagePath);
            Assert.That(second.MissingImages, Is.Empty);
            Assert.That(second.ImageCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void SaveAsAutoStepPackage_NoWorkDir_FallsBackToTempWorkDir()
        {
            StepsFile.WorkDirResolver = () => null;

            var burnStep = new BurnInConfigStep("烧录判定") { GoodTemplateName = "images/burnin_good.png" };
            var steps = new ObservableCollection<AutomationStepBase> { burnStep };
            string packagePath = Path.Combine(_testDir, "nofallback.autostep");

            var result = StepsFile.SaveAsAutoStepPackage(steps, packagePath);

            Assert.That(result.WorkDir, Does.StartWith(StepsFile.TempWorkRoot), "没有工作目录时应落到临时目录");
            Assert.That(Directory.Exists(Path.Combine(_testDir, "nofallback_images")), Is.False);

            StepsFile.TryReleaseTempWorkDir(result.WorkDir);
            Assert.That(Directory.Exists(result.WorkDir), Is.False, "退出时应能删掉本次会话的临时工作目录");
        }

        #endregion

        #region 打开工程解压到临时目录

        [Test]
        public void LoadFromAutoStepPackage_ExtractsIntoTempDir_NotNextToPackage()
        {
            string workDir = Path.Combine(_testDir, "work");
            Directory.CreateDirectory(Path.Combine(workDir, "images"));
            File.WriteAllBytes(Path.Combine(workDir, "images", "burnin_good.png"), TinyPng);
            StepsFile.WorkDirResolver = () => workDir;

            var burnStep = new BurnInConfigStep("烧录判定") { GoodTemplateName = "images/burnin_good.png" };
            string packagePath = Path.Combine(_testDir, "tempdir.autostep");
            StepsFile.SaveAsAutoStepPackage(new ObservableCollection<AutomationStepBase> { burnStep }, packagePath);

            var loaded = StepsFile.LoadFromAutoStepPackage(packagePath);

            Assert.That(loaded.Count, Is.EqualTo(1));
            Assert.That(Directory.Exists(Path.Combine(_testDir, "tempdir_images")), Is.False,
                "打开工程不应在工程文件旁边建 {包名}_images 目录");

            string[] staged = Directory.GetDirectories(StepsFile.TempWorkRoot, "tempdir-*");
            Assert.That(staged, Is.Not.Empty, "解压出来的图片应在临时工作目录里");
            string newest = staged.OrderByDescending(Directory.GetLastWriteTime).First();
            Assert.That(File.Exists(Path.Combine(newest, "images", "burnin_good.png")), Is.True);

            foreach (string dir in staged)
                StepsFile.TryReleaseTempWorkDir(dir);
        }

        #endregion

        #region M23 打开损坏包不破坏现有工作目录

        [Test]
        public void LoadFromAutoStepPackage_CorruptPackage_KeepsExistingWorkDir()
        {
            string workDir = Path.Combine(_testDir, "work");
            Directory.CreateDirectory(workDir);
            StepsFile.WorkDirResolver = () => workDir;

            string marker = Path.Combine(workDir, "keep.txt");
            File.WriteAllText(marker, "keep");

            // 破坏包内容
            string packagePath = Path.Combine(_testDir, "corrupt.autostep");
            File.WriteAllText(packagePath, "not a zip");

            Assert.That(new NUnit.Framework.TestDelegate(() => StepsFile.LoadFromAutoStepPackage(packagePath)), Throws.Exception);

            Assert.That(File.Exists(marker), Is.True, "打开损坏包时不能动当前工作目录");
            Assert.That(File.ReadAllText(marker), Is.EqualTo("keep"));

            if (Directory.Exists(StepsFile.TempWorkRoot))
            {
                Assert.That(Directory.GetDirectories(StepsFile.TempWorkRoot, ".staging-*"), Is.Empty,
                    "解压失败要把临时目录清掉");
            }
        }

        #endregion
    }
}
