using System;
using System.Collections.Generic;
using System.IO;

namespace ShaoLu.Utils
{
    /// <summary>
    /// 图像步骤的原图仓库。
    ///
    /// 规则（2026-11）：
    /// - 截图 / 选出来的原图先放本机临时目录，保存工程包时再按「时间字符串 + 编号」命名打包进包内；
    /// - 包内条目名为 <c>images/{yyyyMMdd_HHmmss}_{编号}.{扩展名}</c>，同一秒内编号递增，避免重名；
    /// - 加载工程包后按该名称在工作目录里找回原图，换机也能继续编辑（不再依赖本机绝对路径）。
    /// </summary>
    public static class StepSourceImageStore
    {
        /// <summary>默认扩展名（截图统一存 PNG）</summary>
        public const string DefaultExtension = ".png";

        /// <summary>包内原图目录</summary>
        public const string PackageFolder = "images";

        private const int MaxProbe = 10000;

        /// <summary>
        /// 原图临时目录：%TEMP%\AutoShaoLu\source_images
        /// </summary>
        public static string TempRoot
        {
            get
            {
                string dir = Path.Combine(Path.GetTempPath(), "AutoShaoLu", "source_images");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// 默认文件名（不含扩展名）：时间字符串 + 编号，如 20261109_113012_1
        /// </summary>
        public static string BuildDefaultBaseName(DateTime time, int index)
        {
            return $"{time:yyyyMMdd_HHmmss}_{index}";
        }

        /// <summary>
        /// 在临时目录里生成一个新的原图路径（只生成名字，不创建文件）。
        /// 同一秒内重复调用时编号递增，不会覆盖已有图片。
        /// </summary>
        public static string CreateTempImagePath(string extension = DefaultExtension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                extension = DefaultExtension;

            string dir = TempRoot;
            var now = DateTime.Now;
            for (int index = 1; index <= MaxProbe; index++)
            {
                string path = Path.Combine(dir, BuildDefaultBaseName(now, index) + extension);
                if (!File.Exists(path))
                    return path;
            }

            // 极端情况（一秒内上万张图）：退回 GUID，保证绝不覆盖
            return Path.Combine(dir, Guid.NewGuid().ToString("N") + extension);
        }

        /// <summary>
        /// 原图对应的包内条目名：images/{文件名}
        /// </summary>
        public static string GetPackageEntryName(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return null;
            return $"{PackageFolder}/{Path.GetFileName(fullPath)}";
        }

        /// <summary>
        /// 生成一个未被占用的包内条目名，并把它登记进 <paramref name="used"/>。
        /// 命名规则同样是「时间字符串 + 编号」（编号递增直到不冲突）。
        /// </summary>
        public static string CreatePackageEntryName(ISet<string> used, string extension = DefaultExtension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                extension = DefaultExtension;

            var now = DateTime.Now;
            for (int index = 1; index <= MaxProbe; index++)
            {
                string entry = $"{PackageFolder}/{BuildDefaultBaseName(now, index)}{extension}";
                if (used == null)
                    return entry;
                if (used.Add(entry))
                    return entry;
            }

            string fallback = $"{PackageFolder}/{Guid.NewGuid():N}{extension}";
            used?.Add(fallback);
            return fallback;
        }

        /// <summary>
        /// 是否位于本程序的原图临时目录（保存后要把路径改指向工程内的副本）
        /// </summary>
        public static bool IsTempPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                string root = Path.GetFullPath(TempRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(path);
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 在资源管理器里选中指定文件（文件不存在时打开其所在目录）
        /// </summary>
        public static void ShowInExplorer(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                if (File.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                    return;
                }

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
            }
            catch
            {
                // 打开资源管理器失败不影响编辑流程
            }
        }
    }
}
