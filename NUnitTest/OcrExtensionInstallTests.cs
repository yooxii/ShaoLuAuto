using NUnit.Framework;
using ShaoLu.Services.Ocr;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace NUnitTest
{
    /// <summary>
    /// OCR 扩展包的检查与安装（「扩展」菜单背后的逻辑）：
    /// 能识别 zip / 文件夹两种来源，能查缺文件、x86 原生库、没有入口类型，且安装后文件确实就位。
    /// </summary>
    [TestFixture]
    public class OcrExtensionInstallTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ShaoLuOcrInstallTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            OcrExtensionHost.ResetForTests();
            try
            {
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
            }
            catch
            {
                // 清理失败不影响用例结论
            }
        }

        /// <summary>已构建的扩展输出目录（没有构建过时返回 null，用例跳过）</summary>
        private static string BuiltExtensionDirectory()
        {
            string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            var candidates = new[]
            {
                Path.Combine(repoRoot, "OcrExtension", "bin", "x64", "Debug"),
                Path.Combine(repoRoot, "OcrExtension", "bin", "x64", "Release"),
            };
            return candidates.FirstOrDefault(dir =>
                Directory.Exists(dir) && File.Exists(Path.Combine(dir, "ShaoLu.OcrExtension.dll")));
        }

        /// <summary>把扩展输出目录复制成一份可随意改坏的副本</summary>
        private string CopyExtensionToTemp(string name, bool includeTessData = true)
        {
            string source = BuiltExtensionDirectory();
            string target = Path.Combine(_tempDir, name);
            CopyDir(source, target);

            if (!includeTessData)
            {
                string tess = Path.Combine(target, "tessdata");
                if (Directory.Exists(tess)) Directory.Delete(tess, true);
            }
            return target;
        }

        private static void CopyDir(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(target, dir.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar)));
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination, true);
            }
        }

        /// <summary>造一个“看起来像 PE”的头，只用于位数检查（machine：0x8664=x64，0x014C=x86）</summary>
        private static byte[] FakePe(ushort machine)
        {
            var bytes = new byte[0x100];
            // e_lfanew @0x3C -> 0x80
            BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);
            bytes[0x80] = (byte)'P';
            bytes[0x81] = (byte)'E';
            BitConverter.GetBytes(machine).CopyTo(bytes, 0x84);
            return bytes;
        }

        [Test]
        public void Validate_BuiltExtensionFolder_IsValid()
        {
            string source = BuiltExtensionDirectory();
            if (source == null)
                Assert.Ignore("未构建 OCR 扩展，跳过检查用例");

            var result = OcrExtensionInstaller.Validate(source);

            Assert.That(result.IsValid, Is.True, string.Join("\n", result.Errors));
            Assert.That(result.CandidateDirectory, Is.EqualTo(source));
            Assert.That(result.FactoryType, Does.Contain("TesseractOcrExtensionFactory"));
            Assert.That(result.Version, Is.Not.Null.And.Not.Empty);
            Assert.That(result.FileCount, Is.GreaterThan(10));
            Assert.That(result.Summary, Does.Contain("MB"));
        }

        [Test]
        public void Validate_ZipWithOcrWrapper_FindsNestedExtensionFolder()
        {
            string source = BuiltExtensionDirectory();
            if (source == null)
                Assert.Ignore("未构建 OCR 扩展，跳过检查用例");

            // 发布包形态：zip 根目录下是 Ocr\...
            string packageRoot = Path.Combine(_tempDir, "package");
            CopyDir(source, Path.Combine(packageRoot, "Ocr"));
            string zipPath = Path.Combine(_tempDir, "ext.zip");
            ZipFile.CreateFromDirectory(packageRoot, zipPath);

            var result = OcrExtensionInstaller.Validate(zipPath);
            try
            {
                Assert.That(result.IsValid, Is.True, string.Join("\n", result.Errors));
                Assert.That(result.CandidateDirectory, Does.EndWith("Ocr"));
                Assert.That(Directory.Exists(result.CandidateDirectory), Is.True);
                Assert.That(result.TempRoot, Is.Not.Null.And.Not.Empty);
            }
            finally
            {
                OcrExtensionInstaller.Cleanup(result);
            }
            Assert.That(Directory.Exists(result.TempRoot), Is.False, "解压临时目录应当在清理后消失");
        }

        [Test]
        public void Validate_MissingTessData_IsRejected()
        {
            if (BuiltExtensionDirectory() == null)
                Assert.Ignore("未构建 OCR 扩展，跳过检查用例");

            string broken = CopyExtensionToTemp("no-tessdata", includeTessData: false);
            var result = OcrExtensionInstaller.Validate(broken);

            Assert.That(result.IsValid, Is.False);
            Assert.That(string.Join("\n", result.Errors), Does.Contain("tessdata"));
        }

        [Test]
        public void Validate_X86NativeLibraryInX64Folder_IsRejected()
        {
            if (BuiltExtensionDirectory() == null)
                Assert.Ignore("未构建 OCR 扩展，跳过检查用例");

            string broken = CopyExtensionToTemp("x86-native");
            File.WriteAllBytes(Path.Combine(broken, "x64", "tesseract55.dll"), FakePe(0x014C));

            var result = OcrExtensionInstaller.Validate(broken);

            Assert.That(result.IsValid, Is.False);
            Assert.That(string.Join("\n", result.Errors), Does.Contain("tesseract55.dll"));
        }

        [Test]
        public void Validate_NoFactoryType_IsRejected()
        {
            if (BuiltExtensionDirectory() == null)
                Assert.Ignore("未构建 OCR 扩展，跳过检查用例");

            string broken = CopyExtensionToTemp("no-factory");
            // 用一份不含 IOcrExtensionFactory 的托管 dll 顶掉入口程序集
            string placeholder = Path.Combine(AppContext.BaseDirectory, "NUnitTest.dll");
            File.Copy(placeholder, Path.Combine(broken, OcrExtensionInstaller.EntryAssemblyFileName), true);
            File.Delete(Path.Combine(broken, "ShaoLu.OcrExtension.pdb"));

            var result = OcrExtensionInstaller.Validate(broken);

            Assert.That(result.IsValid, Is.False);
            Assert.That(string.Join("\n", result.Errors), Does.Contain("IOcrExtensionFactory"));
        }

        [Test]
        public void Validate_EmptyPath_IsRejected()
        {
            var result = OcrExtensionInstaller.Validate(null);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Is.Not.Empty);
        }

        [Test]
        public void Validate_NonZipFile_IsRejected()
        {
            string textFile = Path.Combine(_tempDir, "readme.txt");
            File.WriteAllText(textFile, "not an extension");

            var result = OcrExtensionInstaller.Validate(textFile);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Is.Not.Empty);
        }

        [Test]
        public void Install_CopiesExtensionToTargetDirectory()
        {
            if (BuiltExtensionDirectory() == null)
                Assert.Ignore("未构建 OCR 扩展，跳过安装用例");

            string target = Path.Combine(_tempDir, "extensions", "Ocr");
            OcrExtensionHost.ResetForTests();
            OcrExtensionHost.ExtensionDirectoryOverride = target;

            var result = OcrExtensionInstaller.Validate(BuiltExtensionDirectory());
            Assert.That(result.IsValid, Is.True, string.Join("\n", result.Errors));

            string installed = OcrExtensionInstaller.Install(result);

            Assert.That(installed, Is.EqualTo(target));
            Assert.That(File.Exists(Path.Combine(target, "ShaoLu.OcrExtension.dll")), Is.True);
            Assert.That(File.Exists(Path.Combine(target, "TesseractOCR.dll")), Is.True);
            Assert.That(Directory.GetFiles(Path.Combine(target, "tessdata"), "*.traineddata").Length, Is.GreaterThan(0));
            Assert.That(File.Exists(Path.Combine(target, "x64", "tesseract55.dll")), Is.True);
            Assert.That(Directory.Exists(target + ".old"), Is.False, "安装成功后不应留下备份目录");
        }

        [Test]
        public void Install_OverExistingExtension_ReplacesIt()
        {
            if (BuiltExtensionDirectory() == null)
                Assert.Ignore("未构建 OCR 扩展，跳过安装用例");

            string target = Path.Combine(_tempDir, "extensions", "Ocr");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "stale.txt"), "old version");

            OcrExtensionHost.ResetForTests();
            OcrExtensionHost.ExtensionDirectoryOverride = target;

            var result = OcrExtensionInstaller.Validate(BuiltExtensionDirectory());
            OcrExtensionInstaller.Install(result);

            Assert.That(File.Exists(Path.Combine(target, "stale.txt")), Is.False, "旧版本文件应被替换掉");
            Assert.That(File.Exists(Path.Combine(target, "ShaoLu.OcrExtension.dll")), Is.True);
            Assert.That(Directory.Exists(target + ".old"), Is.False);
        }

        [Test]
        public void Remove_DeletesExtensionDirectory()
        {
            string target = Path.Combine(_tempDir, "extensions", "Ocr");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "ShaoLu.OcrExtension.dll"), "dummy");

            OcrExtensionHost.ResetForTests();
            OcrExtensionHost.ExtensionDirectoryOverride = target;

            OcrExtensionInstaller.Remove();

            Assert.That(Directory.Exists(target), Is.False);
        }
    }
}
