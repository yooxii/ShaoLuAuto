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
        public void SaveAsAutoStepPackage_LandsImagesIntoTargetWorkDir()
        {
            string workDir = Path.Combine(_testDir, "old_work");
            Directory.CreateDirectory(Path.Combine(workDir, "images"));
            File.WriteAllBytes(Path.Combine(workDir, "images", "burnin_good.png"), TinyPng);
            StepsFile.WorkDirResolver = () => workDir;

            var burnStep = new BurnInConfigStep("烧录判定") { GoodTemplateName = "images/burnin_good.png" };
            var steps = new ObservableCollection<AutomationStepBase> { burnStep };

            string packagePath = Path.Combine(_testDir, "saveas.autostep");
            StepsFile.SaveAsAutoStepPackage(steps, packagePath);

            string targetWorkDir = StepsFile.GetWorkDirPath(packagePath);
            Assert.That(Directory.Exists(targetWorkDir), Is.True);
            Assert.That(File.Exists(Path.Combine(targetWorkDir, "images", "burnin_good.png")), Is.True,
                "另存为后图片必须落到新工作目录，否则第二次保存会剔除图片");

            // 第二次保存（模拟用户再按 Ctrl+S）：包里图片数量不减少
            var second = StepsFile.SaveAsAutoStepPackage(steps, packagePath);
            Assert.That(second.MissingImages, Is.Empty);
            Assert.That(second.ImageCount, Is.GreaterThanOrEqualTo(1));
        }

        #endregion

        #region M23 打开损坏包不破坏现有工作目录

        [Test]
        public void LoadFromAutoStepPackage_CorruptPackage_KeepsExistingWorkDir()
        {
            var steps = new ObservableCollection<AutomationStepBase> { new TypeTextStep("步骤A") };
            StepsFile.WorkDirResolver = () => _testDir;
            string packagePath = Path.Combine(_testDir, "corrupt.autostep");

            StepsFile.SaveAsAutoStepPackage(steps, packagePath);
            string workDir = StepsFile.GetWorkDirPath(packagePath);
            // 无图片的工程不会创建 _images 目录，这里显式建立并放一个标记文件来验证“打开损坏包不会先删目录”
            Directory.CreateDirectory(workDir);
            Assert.That(Directory.Exists(workDir), Is.True);

            string marker = Path.Combine(workDir, "keep.txt");
            File.WriteAllText(marker, "keep");

            // 破坏包内容
            File.WriteAllText(packagePath, "not a zip");

            Assert.That(new NUnit.Framework.TestDelegate(() => StepsFile.LoadFromAutoStepPackage(packagePath)), Throws.Exception);

            Assert.That(File.Exists(marker), Is.True, "打开损坏包时不能先删掉当前工作目录");
            Assert.That(File.ReadAllText(marker), Is.EqualTo("keep"));
        }

        #endregion
    }
}
