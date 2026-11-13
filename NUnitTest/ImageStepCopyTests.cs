using NUnit.Framework;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace NUnitTest
{
    /// <summary>
    /// 复制图像步骤后的图片归属与工程包完整性。
    /// 克隆体会继承原步骤的裁剪图条目名，两条步骤抢同一个文件：
    /// 工程包里出现同名条目会导致解压失败（重新打开工程报图片不存在），
    /// 图片编辑窗口「没重新裁剪直接保存」也必须把图片落到克隆体自己的名字下。
    /// </summary>
    [TestFixture]
    [Apartment(System.Threading.ApartmentState.STA)]
    public class ImageStepCopyTests
    {
        /// <summary>1×1 的 PNG，测试用最小图片</summary>
        private static readonly byte[] TinyPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        /// <summary>
        /// Ioc.Default 内部的 IServiceProvider 字段。
        /// CommunityToolkit 的 Ioc 只允许 ConfigureServices 一次（本进程里可能已被其它用例配置过），
        /// 这里用反射注入并还原 `ImageRecognitionBase` 需要的 MainViewModel / FileServices，避免触发单次配置限制。
        /// </summary>
        private static readonly System.Reflection.FieldInfo IocProviderField =
            typeof(CommunityToolkit.Mvvm.DependencyInjection.Ioc)
                .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .FirstOrDefault(f => typeof(IServiceProvider).IsAssignableFrom(f.FieldType));

        private object _originalIocProvider;
        private bool _originalIocProviderCaptured;
        private MainViewModel _main;
        private string _testDir;
        private string _workDir;

        [SetUp]
        public void SetUp()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "ShaoLuImageStepCopyTests", Guid.NewGuid().ToString("N"));
            _workDir = Path.Combine(_testDir, "proj_images");
            Directory.CreateDirectory(Path.Combine(_workDir, "images"));

            _main = new MainViewModel { StepImageWorkDir = _workDir };
            if (IocProviderField != null)
            {
                _originalIocProvider = IocProviderField.GetValue(CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default);
                _originalIocProviderCaptured = true;
                IocProviderField.SetValue(
                    CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default,
                    new TestServices(_main));
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (IocProviderField != null && _originalIocProviderCaptured)
            {
                IocProviderField.SetValue(CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default, _originalIocProvider);
                _originalIocProvider = null;
                _originalIocProviderCaptured = false;
            }

            try
            {
                if (Directory.Exists(_testDir))
                    Directory.Delete(_testDir, true);
            }
            catch
            {
                // 临时目录清理失败不影响用例结论
            }
        }

        /// <summary>建一个已经裁好图（图片文件已落盘）的识图步骤</summary>
        private ClickImageStep CreateStepWithCroppedImage(string name)
        {
            var step = new ClickImageStep(name);
            string entryName = $"images/{step.Uid}.png";
            string fullPath = Path.Combine(_workDir, entryName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, TinyPng);
            step.CroppedImageName = entryName;
            return step;
        }

        /// <summary>模拟复制/粘贴：深拷贝 + 新 Uid（与原步骤共用同一个裁剪图条目名）</summary>
        private static ClickImageStep CloneAsCopy(ClickImageStep source)
        {
            var copy = (ClickImageStep)source.Clone();
            copy.Uid = Guid.NewGuid();
            return copy;
        }

        [Test]
        public void CopiedImageStep_SavePackage_GivesEachStepItsOwnImage()
        {
            var original = CreateStepWithCroppedImage("原步骤");
            var copy = CloneAsCopy(original);
            Assert.That(copy.CroppedImageName, Is.EqualTo(original.CroppedImageName),
                "前置条件：复制出来的步骤一开始与原步骤共用同一个图片条目名");

            var steps = new ObservableCollection<AutomationStepBase> { original, copy };
            string packagePath = Path.Combine(_testDir, "copy.autostep");

            var result = StepsFile.SaveAsAutoStepPackage(steps, packagePath);

            Assert.That(result.MissingImages, Is.Empty, "两条步骤的图片都在磁盘上，不应有缺失告警");
            Assert.That(copy.CroppedImageName, Is.Not.EqualTo(original.CroppedImageName),
                "复制出来的步骤必须拿到自己的图片条目名");
            Assert.That(File.Exists(copy.CroppedImageFullPath), Is.True, "改名后的图片文件必须真实存在");

            using (var zip = ZipFile.OpenRead(packagePath))
            {
                var entryNames = zip.Entries.Select(e => e.FullName).ToList();
                Assert.That(entryNames.Count, Is.EqualTo(entryNames.Distinct(StringComparer.OrdinalIgnoreCase).Count()),
                    "工程包里不能出现同名条目，否则解压会失败");
                Assert.That(entryNames, Does.Contain(original.CroppedImageName));
                Assert.That(entryNames, Does.Contain(copy.CroppedImageName));
            }
        }

        [Test]
        public void CopiedImageStep_ReopenPackage_BothStepsKeepTheirImage()
        {
            var original = CreateStepWithCroppedImage("原步骤");
            var copy = CloneAsCopy(original);
            var steps = new ObservableCollection<AutomationStepBase> { original, copy };
            string packagePath = Path.Combine(_testDir, "copy-reopen.autostep");

            StepsFile.SaveAsAutoStepPackage(steps, packagePath);
            var loaded = StepsFile.LoadFromAutoStepPackage(packagePath);

            Assert.That(loaded.Count, Is.EqualTo(2));
            foreach (var step in loaded)
            {
                var candidate = step as ClickImageStep;
                Assert.That(candidate, Is.Not.Null);
                Assert.That(candidate.CroppedImageName, Is.Not.Null.And.Not.Empty);
                Assert.That(File.Exists(candidate.CroppedImageFullPath), Is.True,
                    $"重新打开后图片必须存在：{candidate.CroppedImageFullPath}");
            }

            var missing = StepsFile.CollectMissingAssets(loaded, _main.StepImageWorkDir);
            Assert.That(missing, Is.Empty, "重新打开后不应再报图片缺失");
        }

        [Test]
        public void CopiedImageStep_EditorSaveWithoutRecrop_WritesOwnImageFile()
        {
            var original = CreateStepWithCroppedImage("原步骤");
            var copy = CloneAsCopy(original);

            // 图片编辑窗口保存时 ImgDst 直接就是步骤缓存里的那张图（用户没有重新裁剪）
            var cached = copy.CroppedImg;
            Assert.That(cached, Is.Not.Null, "前置条件：复制出来的步骤能从原步骤的图片文件读出预览");

            copy.CroppedImg = cached;

            Assert.That(copy.LastCroppedImageSaved, Is.True, "保存裁剪图必须真实落盘成功");
            Assert.That(copy.CroppedImageName, Is.EqualTo($"images/{copy.Uid}.png"),
                "落盘后条目名必须换成自己的 Uid，不能继续引用原步骤的图片");
            Assert.That(File.Exists(copy.CroppedImageFullPath), Is.True);
            Assert.That(File.Exists(original.CroppedImageFullPath), Is.True, "原步骤的图片不能被覆盖或删除");
        }

        [Test]
        public void SingleImageStep_EditorSaveTwice_ImageStaysOnDisk()
        {
            var step = CreateStepWithCroppedImage("原步骤");
            var cached = step.CroppedImg;

            // 同一张图连续保存两次（引用相同）：第二次也必须保持图片可用
            step.CroppedImg = cached;
            step.CroppedImg = cached;

            Assert.That(step.LastCroppedImageSaved, Is.True);
            Assert.That(File.Exists(step.CroppedImageFullPath), Is.True);
            Assert.That(step.CroppedImageName, Is.EqualTo($"images/{step.Uid}.png"));
        }

        private sealed class TestServices : IServiceProvider
        {
            private readonly MainViewModel _main;
            private readonly FileServices _files = new FileServices();

            public TestServices(MainViewModel main)
            {
                _main = main;
            }

            public object GetService(Type serviceType)
            {
                if (serviceType == typeof(MainViewModel)) return _main;
                if (serviceType == typeof(FileServices)) return _files;
                return null;
            }
        }
    }
}
