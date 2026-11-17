using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace ShaoLu.Services.Ocr
{
    /// <summary>
    /// OCR 扩展包的检查结果。
    /// <see cref="CandidateDirectory"/> 是真正要复制到 <c>extensions\Ocr</c> 的那一层目录。
    /// </summary>
    public sealed class OcrExtensionValidationResult
    {
        /// <summary>检查是否通过（<see cref="Errors"/> 为空即通过）</summary>
        public bool IsValid => Errors.Count == 0;

        /// <summary>扩展文件所在目录（含 ShaoLu.OcrExtension.dll / tessdata / x64）</summary>
        public string CandidateDirectory { get; internal set; }

        /// <summary>zip 解压出来的临时目录（安装结束后由调用方清理）</summary>
        public string TempRoot { get; internal set; }

        /// <summary>检查未通过的原因（可直接展示给用户）</summary>
        public List<string> Errors { get; } = new List<string>();

        /// <summary>不影响安装但值得提示的信息</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>扩展程序集版本（读取失败时为空）</summary>
        public string Version { get; internal set; }

        /// <summary>扩展入口类型全名（读取失败时为空）</summary>
        public string FactoryType { get; internal set; }

        /// <summary>本次要安装的文件数与总字节数</summary>
        public int FileCount { get; internal set; }
        public long TotalBytes { get; internal set; }

        /// <summary>给用户看的一句话摘要（版本 / 文件数 / 体积）</summary>
        public string Summary { get; internal set; }
    }

    /// <summary>
    /// OCR 扩展包的检查与安装：
    /// - 支持「扩展包 .zip」与「已解压的文件夹」两种来源，自动定位其中的 Ocr 目录；
    /// - 检查必备文件（入口程序集、TesseractOCR、x64 原生库、语言数据）与原生库位数；
    /// - 检查入口程序集里确实有 IOcrExtensionFactory 实现（反射只读加载，不执行扩展代码）；
    /// - 安装前把旧扩展改名备份，复制失败时回滚，避免把能用的扩展弄坏。
    /// </summary>
    public static class OcrExtensionInstaller
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>扩展包内的入口程序集文件名（缺它也可以，只要能找到 IOcrExtensionFactory 实现）</summary>
        public const string EntryAssemblyFileName = "ShaoLu.OcrExtension.dll";

        /// <summary>扩展安装目标目录：<c>{程序目录}\extensions\Ocr</c></summary>
        public static string TargetDirectory => OcrExtensionHost.ExtensionDirectory;

        #region 检查

        /// <summary>检查用户选中的扩展包（.zip 文件或文件夹）</summary>
        public static OcrExtensionValidationResult Validate(string path)
        {
            var result = new OcrExtensionValidationResult();
            if (string.IsNullOrWhiteSpace(path))
            {
                result.Errors.Add(LanguageService.GetLocalizedString("Ext_NoPathSelected", "没有选择扩展包。"));
                return result;
            }

            try
            {
                string sourceDir;
                if (File.Exists(path))
                {
                    if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Errors.Add(string.Format(
                            LanguageService.GetLocalizedString("Ext_NotZip", "只支持扩展包（.zip）或已解压的文件夹：{0}"), path));
                        return result;
                    }

                    result.TempRoot = Path.Combine(Path.GetTempPath(), "AutoShaoLu", "ocr-extension-" + Guid.NewGuid().ToString("N"));
                    ExtractZip(path, result.TempRoot);
                    sourceDir = result.TempRoot;
                }
                else if (Directory.Exists(path))
                {
                    sourceDir = path;
                }
                else
                {
                    result.Errors.Add(string.Format(
                        LanguageService.GetLocalizedString("Ext_FolderMissing", "目录不存在：{0}"), path));
                    return result;
                }

                // 扩展包有两种常见形态：包根目录下就是 Ocr 文件夹，或者选中的就是 Ocr 文件夹本身
                string candidate = FindExtensionRoot(sourceDir);
                if (candidate == null)
                {
                    result.Errors.Add(LanguageService.GetLocalizedString(
                        "Ext_NoOcrFolder",
                        "扩展包里没有找到 OCR 扩展（应当包含 Ocr\\ShaoLu.OcrExtension.dll、tessdata 与 x64 目录）。"));
                    return result;
                }

                result.CandidateDirectory = candidate;
                CheckFiles(result, candidate);
                if (!result.IsValid) return result;

                CheckNativeBitness(result, candidate);
                ProbeManagedEntry(result, candidate);
                FillSummary(result, candidate);
                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Validate OCR extension failed: {0}", path);
                result.Errors.Add(string.Format(
                    LanguageService.GetLocalizedString("Ext_ReadFailed", "读取扩展包失败：{0}"), ex.Message));
                return result;
            }
        }

        /// <summary>在解压目录里找真正存放扩展文件的目录（最多向下找 3 层）</summary>
        private static string FindExtensionRoot(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;

            // 优先精确命中入口程序集
            string exact = FindDirectory(root, dir => File.Exists(Path.Combine(dir, EntryAssemblyFileName)), 3);
            if (exact != null) return exact;

            // 退一步：有 TesseractOCR.dll + tessdata 也算（改过名的自定义扩展）
            return FindDirectory(root, dir =>
                File.Exists(Path.Combine(dir, "TesseractOCR.dll")) &&
                Directory.Exists(Path.Combine(dir, "tessdata")), 3);
        }

        private static string FindDirectory(string root, Func<string, bool> predicate, int maxDepth)
        {
            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((root, 0));
            while (queue.Count > 0)
            {
                var (dir, depth) = queue.Dequeue();
                if (predicate(dir)) return dir;
                if (depth >= maxDepth) continue;

                try
                {
                    foreach (var child in Directory.GetDirectories(dir))
                        queue.Enqueue((child, depth + 1));
                }
                catch
                {
                    // 个别目录无权限时跳过，继续找下一层
                }
            }
            return null;
        }

        private static void CheckFiles(OcrExtensionValidationResult result, string dir)
        {
            string tessData = Path.Combine(dir, "tessdata");
            string nativeX64 = Path.Combine(dir, "x64");

            void Require(string relative, string reasonKey, string fallback)
            {
                if (!File.Exists(Path.Combine(dir, relative)))
                    result.Errors.Add(string.Format(LanguageService.GetLocalizedString(reasonKey, fallback), relative));
            }

            Require("TesseractOCR.dll", "Ext_MissingFile", "扩展包缺少文件：{0}");
            Require(Path.Combine("x64", "tesseract55.dll"), "Ext_MissingFile", "扩展包缺少文件：{0}");
            Require(Path.Combine("x64", "leptonica-1.85.0.dll"), "Ext_MissingFile", "扩展包缺少文件：{0}");
            Require(Path.Combine("tessdata", "eng.traineddata"), "Ext_MissingFile", "扩展包缺少文件：{0}");

            if (!Directory.Exists(nativeX64))
                result.Errors.Add(string.Format(LanguageService.GetLocalizedString("Ext_MissingFolder", "扩展包缺少目录：{0}"), "x64"));

            if (!Directory.Exists(tessData))
            {
                result.Errors.Add(string.Format(LanguageService.GetLocalizedString("Ext_MissingFolder", "扩展包缺少目录：{0}"), "tessdata"));
            }
            else if (!Directory.GetFiles(tessData, "*.traineddata").Any())
            {
                result.Errors.Add(LanguageService.GetLocalizedString("Ext_NoLanguageData", "tessdata 目录里没有语言数据（*.traineddata）。"));
            }

            if (Directory.Exists(Path.Combine(dir, "x86")) && !Directory.Exists(nativeX64))
                result.Warnings.Add(LanguageService.GetLocalizedString("Ext_LooksX86", "看起来是 x86 版本的扩展，主程序是 x64，将无法加载。"));
        }

        /// <summary>原生库必须是 x64（主程序是 x64，32 位 DLL 加载会直接失败）</summary>
        private static void CheckNativeBitness(OcrExtensionValidationResult result, string dir)
        {
            foreach (var name in new[] { "tesseract55.dll", "leptonica-1.85.0.dll" })
            {
                string path = Path.Combine(dir, "x64", name);
                if (!File.Exists(path)) continue;

                if (TryReadPeMachine(path, out ushort machine))
                {
                    if (machine != 0x8664) // IMAGE_FILE_MACHINE_AMD64
                    {
                        result.Errors.Add(string.Format(
                            LanguageService.GetLocalizedString("Ext_NativeNotX64", "{0} 不是 x64 原生库（machine=0x{1:X4}），主程序无法加载。"),
                            name, machine));
                    }
                }
                else
                {
                    result.Warnings.Add(string.Format(
                        LanguageService.GetLocalizedString("Ext_NativeUnreadable", "无法读取 {0} 的 PE 头，已跳过位数检查。"), name));
                }
            }
        }

        /// <summary>只读加载扩展程序集，确认里面有 IOcrExtensionFactory 实现（不执行扩展代码）</summary>
        private static void ProbeManagedEntry(OcrExtensionValidationResult result, string dir)
        {
            // 只探测“入口候选”：优先固定名字，其次文件名带 Ocr 的程序集。
            // 依赖程序集（TesseractOCR / System.* / Microsoft.*）不参与，避免把依赖当成入口误判。
            var candidates = new List<string>();
            string entry = Path.Combine(dir, EntryAssemblyFileName);
            if (File.Exists(entry))
            {
                candidates.Add(entry);
            }
            else
            {
                candidates.AddRange(Directory.GetFiles(dir, "*.dll")
                    .Where(p => Path.GetFileNameWithoutExtension(p).IndexOf("ocr", StringComparison.OrdinalIgnoreCase) >= 0));
            }

            var searched = new List<string>();
            foreach (var dll in candidates)
            {
                searched.Add(Path.GetFileName(dll));
                var probe = ProbeAssemblyIsolated(dll);
                if (probe == null) continue;

                result.FactoryType = probe.Value.FactoryType;
                result.Version = probe.Value.Version;
                return;
            }

            result.Errors.Add(string.Format(
                LanguageService.GetLocalizedString("Ext_NoEntryType",
                    "扩展包里没有可用的 OCR 扩展入口（找不到 IOcrExtensionFactory 实现，已检查：{0}）。"),
                searched.Count == 0 ? LanguageService.GetLocalizedString("Ext_NoDll", "目录里没有 dll") : string.Join("、", searched)));
        }

        /// <summary>
        /// 在独立的 AppDomain 里加载扩展程序集，确认里面有 IOcrExtensionFactory 实现。
        ///
        /// 不能在本进程直接反射加载：同一进程里已经加载过（或同时装两个副本时）同一程序集标识，
        /// 会撞上「已从其他位置加载」的限制；独立域用完即卸载，装几次都不冲突，也不会执行扩展的初始化代码
        /// （只取类型信息，不创建引擎）。
        /// </summary>
        private static (string FactoryType, string Version)? ProbeAssemblyIsolated(string dllPath)
        {
            AppDomain domain = null;
            try
            {
                var setup = new AppDomainSetup
                {
                    ApplicationBase = AppContext.BaseDirectory,
                    LoaderOptimization = LoaderOptimization.SingleDomain,
                };

                domain = AppDomain.CreateDomain("ShaoLuOcrExtensionProbe", null, setup);
                var probe = (OcrExtensionAssemblyProbe)domain.CreateInstanceAndUnwrap(
                    typeof(OcrExtensionAssemblyProbe).Assembly.FullName,
                    typeof(OcrExtensionAssemblyProbe).FullName);

                string result = probe.Probe(dllPath);
                if (string.IsNullOrEmpty(result)) return null;

                var parts = result.Split('\t');
                if (parts.Length < 2) return null;
                return (parts[0], parts[1]);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Probe OCR extension assembly failed: {0}", dllPath);
                return null;
            }
            finally
            {
                if (domain != null)
                {
                    try { AppDomain.Unload(domain); }
                    catch (Exception ex) { logger.Warn(ex, "Unload probe domain failed"); }
                }
            }
        }

        private static bool TryReadPeMachine(string path, out ushort machine)
        {
            machine = 0;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new BinaryReader(stream))
                {
                    if (stream.Length < 0x40) return false;
                    stream.Position = 0x3C;
                    int peOffset = reader.ReadInt32();
                    if (peOffset <= 0 || peOffset + 6 > stream.Length) return false;

                    stream.Position = peOffset;
                    if (reader.ReadUInt32() != 0x00004550) return false; // "PE\0\0"
                    machine = reader.ReadUInt16();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void FillSummary(OcrExtensionValidationResult result, string dir)
        {
            try
            {
                var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                result.FileCount = files.Length;
                result.TotalBytes = files.Sum(f => new FileInfo(f).Length);
            }
            catch
            {
                // 统计失败不影响安装
            }

            string version = string.IsNullOrEmpty(result.Version) ? "-" : result.Version;
            result.Summary = string.Format(
                LanguageService.GetLocalizedString("Ext_Summary", "版本 {0}，{1} 个文件，{2} MB"),
                version, result.FileCount, Math.Round(result.TotalBytes / 1024d / 1024d, 1));
        }

        private static void ExtractZip(string zipPath, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                string root = Path.GetFullPath(targetDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // 目录条目按需创建

                    string destination = Path.GetFullPath(Path.Combine(
                        targetDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));

                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"扩展包里存在非法路径：{entry.FullName}");

                    string dir = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    // 同名条目以最后一次为准（与工程包解压保持一致）
                    entry.ExtractToFile(destination, overwrite: true);
                }
            }
        }

        #endregion

        #region 安装 / 移除

        /// <summary>
        /// 把检查通过的扩展复制到 <c>extensions\Ocr</c>。旧扩展先改名备份，复制失败自动回滚。
        /// </summary>
        /// <returns>实际安装到的目录</returns>
        public static string Install(OcrExtensionValidationResult result)
        {
            if (result == null || !result.IsValid || string.IsNullOrEmpty(result.CandidateDirectory))
                throw new InvalidOperationException(LanguageService.GetLocalizedString("Ext_NotValidated", "扩展包还没有通过检查。"));

            string source = result.CandidateDirectory;
            string target = TargetDirectory;
            string parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);

            string backup = target + ".old";
            bool backedUp = false;
            try
            {
                if (Directory.Exists(backup))
                    Directory.Delete(backup, true);

                if (Directory.Exists(target))
                {
                    Directory.Move(target, backup);
                    backedUp = true;
                }

                CopyDirectory(source, target);

                // 复制完成后的基本校验：入口程序集必须就位
                if (!File.Exists(Path.Combine(target, EntryAssemblyFileName)) &&
                    !Directory.GetFiles(target, "*.dll").Any())
                {
                    throw new InvalidOperationException(LanguageService.GetLocalizedString("Ext_CopyIncomplete", "安装后的扩展文件不完整。"));
                }
            }
            catch
            {
                // 回滚：把备份换回原位，避免连原来能用的扩展都没了
                try
                {
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    if (backedUp && Directory.Exists(backup)) Directory.Move(backup, target);
                }
                catch (Exception rollbackEx)
                {
                    logger.Error(rollbackEx, "Rollback OCR extension install failed: {0}", target);
                }
                throw;
            }

            // 安装成功：清掉备份（体积有 100 MB 左右，不留双份）
            try
            {
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Delete OCR extension backup failed: {0}", backup);
            }

            logger.Info("OCR extension installed: {0} <- {1} ({2})", target, source, result.Summary);
            return target;
        }

        /// <summary>移除 <c>extensions\Ocr</c>（先改名再删，让文件占用/DLL 锁定时也能给出明确结果）</summary>
        public static void Remove()
        {
            string target = TargetDirectory;
            if (!Directory.Exists(target)) return;

            string trash = target + ".removing-" + Guid.NewGuid().ToString("N");
            Directory.Move(target, trash);
            try
            {
                Directory.Delete(trash, true);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Delete removed OCR extension failed: {0}", trash);
            }
            logger.Info("OCR extension removed: {0}", target);
        }

        /// <summary>清理 zip 解压出来的临时目录</summary>
        public static void Cleanup(OcrExtensionValidationResult result)
        {
            if (result?.TempRoot == null) return;
            try
            {
                if (Directory.Exists(result.TempRoot))
                    Directory.Delete(result.TempRoot, true);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Cleanup OCR extension temp failed: {0}", result.TempRoot);
            }
            finally
            {
                result.TempRoot = null;
            }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string relative = dir.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(target, relative));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetExtension(file), ".pdb", StringComparison.OrdinalIgnoreCase))
                    continue; // 调试符号不需要

                string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
                string destination = Path.Combine(target, relative);
                string dir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.Copy(file, destination, overwrite: true);
            }
        }

        #endregion
    }

    /// <summary>
    /// 跨域探针：在独立的 AppDomain 里加载扩展程序集并找出 IOcrExtensionFactory 实现。
    /// 只返回「类型全名 + 版本」字符串，跨域不传任何扩展类型实例。
    /// </summary>
    public sealed class OcrExtensionAssemblyProbe : MarshalByRefObject
    {
        /// <summary>返回 "类型全名\t版本"，找不到时返回 null</summary>
        public string Probe(string dllPath)
        {
            string probeDir = Path.GetDirectoryName(dllPath);
            ResolveEventHandler handler = (sender, args) =>
            {
                try
                {
                    string simpleName = new AssemblyName(args.Name).Name;
                    foreach (var dir in new[] { probeDir, AppContext.BaseDirectory })
                    {
                        if (string.IsNullOrEmpty(dir)) continue;
                        string candidate = Path.Combine(dir, simpleName + ".dll");
                        if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
                    }
                }
                catch
                {
                    // 依赖解析失败按“找不到”处理，交给上层报错
                }
                return null;
            };

            AppDomain.CurrentDomain.AssemblyResolve += handler;
            try
            {
                var asm = Assembly.LoadFrom(dllPath);
                foreach (var type in asm.GetExportedTypes())
                {
                    if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null) continue;
                    if (!type.GetInterfaces().Any(i =>
                        string.Equals(i.FullName, "ShaoLu.Services.Ocr.IOcrExtensionFactory", StringComparison.Ordinal)))
                        continue;

                    var version = asm.GetName().Version;
                    return type.FullName + "\t" + (version?.ToString(3) ?? string.Empty);
                }
                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= handler;
            }
        }
    }
}
