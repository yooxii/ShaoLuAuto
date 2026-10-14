using NUnit.Framework;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NUnitTest
{
    /// <summary>
    /// 子线程图像步骤的跨线程取图测试：
    /// 未冻结的界面线程图像赋值给 CroppedImg 后仍能拿到可用 Bitmap；文件路径优先，回退与异常语义保持不变。
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ImageThreadingTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ShaoLuImageThreadingTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
            }
            catch
            {
                // 临时目录清理失败不影响用例结论
            }
        }

        /// <summary>在磁盘上生成指定尺寸的 PNG，返回路径（保存后立即释放文件句柄）</summary>
        private string WritePngFile(string fileName, int width, int height)
        {
            string path = Path.Combine(_tempDir, fileName);
            using (var bmp = new Bitmap(width, height))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.Clear(System.Drawing.Color.CornflowerBlue);
                bmp.Save(path, ImageFormat.Png);
            }
            return path;
        }

        /// <summary>
        /// 构造界面线程创建但未冻结的 BitmapImage（BeginInit / CacheOption=OnLoad / StreamSource / EndInit，故意不 Freeze）
        /// </summary>
        private static BitmapImage CreateUnfrozenBitmapImage(int width, int height)
        {
            byte[] pngBytes;
            using (var bmp = new Bitmap(width, height))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.Clear(System.Drawing.Color.CornflowerBlue);
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngBytes = ms.ToArray();
                }
            }

            var image = new BitmapImage();
            using (var stream = new MemoryStream(pngBytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }
            Assert.That(((System.Windows.Freezable)image).IsFrozen, Is.False, "测试前置条件：BitmapImage 必须处于未冻结状态");
            return image;
        }

        /// <summary>在另一个 STA 线程上执行取图，返回异常与结果（模拟运行时段的后台线程）</summary>
        private static (System.Drawing.Bitmap Bitmap, Exception Failure) RunOnOtherThread(Func<System.Drawing.Bitmap> action)
        {
            System.Drawing.Bitmap bitmap = null;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    bitmap = action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.That(thread.Join(30000), Is.True, "取图线程超时未结束");
            return (bitmap, failure);
        }

        [Test]
        public void FreezeIfPossible_UnfrozenImage_BecomesFrozenAndIsIdempotent()
        {
            var unfrozen = CreateUnfrozenBitmapImage(4, 4);

            Autogui.FreezeIfPossible(unfrozen);
            Assert.That(unfrozen.IsFrozen, Is.True);

            // 已冻结与 null 都必须是无害调用
            Autogui.FreezeIfPossible(unfrozen);
            Autogui.FreezeIfPossible(null);
            Assert.That(unfrozen.IsFrozen, Is.True);
        }

        [Test]
        public void CroppedImg_AssignedUnfrozenImage_IsFrozenAndUsableAsBitmap()
        {
            var step = new FindImageStep("子线程识图");
            var unfrozen = CreateUnfrozenBitmapImage(12, 8);

            step.CroppedImg = unfrozen;

            // 缓存交给运行时段的必须是冻结对象，不再依赖界面线程那个未冻结的 ImageSource
            var cached = step.CroppedImg as BitmapSource;
            Assert.That(cached, Is.Not.Null);
            Assert.That(cached.IsFrozen, Is.True);

            var (bitmap, failure) = RunOnOtherThread(() => Autogui.CreateRuntimeBitmap(step.CroppedImg, new[] { step.CroppedImageFullPath }));
            Assert.That(failure, Is.Null, failure?.ToString());
            Assert.That(bitmap, Is.Not.Null);
            try
            {
                Assert.That(bitmap.Width, Is.EqualTo(12));
                Assert.That(bitmap.Height, Is.EqualTo(8));
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        [Test]
        public void CroppedImg_FrozenCachedImage_IsConvertibleFromAnotherThreadWithoutFilePaths()
        {
            var step = new FindImageStep("跨线程识图");
            step.CroppedImg = CreateUnfrozenBitmapImage(12, 8);

            // 不提供任何文件路径，强制走 WPF 图像转换：只有已冻结的缓存对象才能被其它线程转换
            var (bitmap, failure) = RunOnOtherThread(() => Autogui.CreateRuntimeBitmap(step.CroppedImg, null));

            Assert.That(failure, Is.Null, failure?.ToString());
            Assert.That(bitmap, Is.Not.Null);
            try
            {
                Assert.That(bitmap.Width, Is.EqualTo(12));
                Assert.That(bitmap.Height, Is.EqualTo(8));
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        [Test]
        public void TryCreateBitmapFromPaths_ExistingFile_LoadsCorrectPixelsAndDoesNotLockFile()
        {
            string path = WritePngFile("crop.png", 24, 16);

            using (var bitmap = Autogui.TryCreateBitmapFromPaths(new[] { path }))
            {
                Assert.That(bitmap, Is.Not.Null);
                Assert.That(bitmap.Width, Is.EqualTo(24));
                Assert.That(bitmap.Height, Is.EqualTo(16));
            }

            // 文件句柄已释放：能改名说明没有锁住文件
            string moved = path + ".moved";
            File.Move(path, moved);
            Assert.That(File.Exists(moved), Is.True);
        }

        [Test]
        public void TryCreateBitmapFromPaths_FirstPathMissing_UsesNextExistingPath()
        {
            string valid = WritePngFile("second.png", 7, 3);
            string missing = Path.Combine(_tempDir, "first.png");

            using (var bitmap = Autogui.TryCreateBitmapFromPaths(new[] { missing, valid }))
            {
                Assert.That(bitmap, Is.Not.Null);
                Assert.That(bitmap.Width, Is.EqualTo(7));
                Assert.That(bitmap.Height, Is.EqualTo(3));
            }
        }

        [Test]
        public void CreateRuntimeBitmap_MissingFile_FallsBackToWpfConversion()
        {
            var frozen = CreateUnfrozenBitmapImage(9, 5);
            frozen.Freeze();
            string missing = Path.Combine(_tempDir, "not-exists.png");

            using (var bitmap = Autogui.CreateRuntimeBitmap(frozen, new[] { missing }))
            {
                Assert.That(bitmap, Is.Not.Null);
                Assert.That(bitmap.Width, Is.EqualTo(9));
                Assert.That(bitmap.Height, Is.EqualTo(5));
            }
        }

        [Test]
        public void CreateRuntimeBitmap_NoFilePathAndNoImage_ThrowsImageConvertError()
        {
            string missing = Path.Combine(_tempDir, "missing.png");

            var ex = Assert.Throws<Exception>((TestDelegate)(() => Autogui.CreateRuntimeBitmap(null, new[] { missing })));
            Assert.That(ex.Message, Is.EqualTo("Image Convert Error."));
        }

        [Test]
        public void ConvertImageSourceToBitmap_UnfrozenImageOnOwningThread_ReturnsBitmap()
        {
            var unfrozen = CreateUnfrozenBitmapImage(10, 6);

            using (var bitmap = Autogui.ConvertImageSourceToBitmap(unfrozen))
            {
                Assert.That(bitmap, Is.Not.Null);
                Assert.That(bitmap.Width, Is.EqualTo(10));
                Assert.That(bitmap.Height, Is.EqualTo(6));
            }
        }

        [Test]
        public void ConvertImageSourceToBitmap_UnfrozenImageFromOtherThread_DoesNotThrowAndReturnsNull()
        {
            var unfrozen = CreateUnfrozenBitmapImage(10, 10);

            // 未冻结对象的跨线程转换按既有语义返回 null（不抛异常），日志中记录一次转换失败
            var (bitmap, failure) = RunOnOtherThread(() => Autogui.ConvertImageSourceToBitmap(unfrozen));

            Assert.That(failure, Is.Null, failure?.ToString());
            Assert.That(bitmap, Is.Null);
        }
    }
}
