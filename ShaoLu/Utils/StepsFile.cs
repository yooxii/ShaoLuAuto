using ShaoLu.Converters;
using ShaoLu.Services;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShaoLu.Utils
{
    /// <summary>
    /// 保存结果：图片落地数量、缺失图片与告警（供 UI 提示，不再静默忽略）
    /// </summary>
    public sealed class SavePackageResult
    {
        /// <summary>包对应的工作目录（{包名}_images）</summary>
        public string WorkDir { get; internal set; }

        /// <summary>已写入包的图片数量</summary>
        public int ImageCount { get; internal set; }

        /// <summary>缺失的图片（源文件不存在），格式：{条目名} ← {期望路径}</summary>
        public List<string> MissingImages { get; } = new();

        /// <summary>图片落地到工作目录时的告警</summary>
        public List<string> Warnings { get; } = new();

        /// <summary>上一版备份（.bak），不存在时为 null</summary>
        public string BackupPath { get; internal set; }

        public bool HasMissingImages => MissingImages.Count > 0;
        public bool HasWarnings => Warnings.Count > 0;
    }

    public class StepsFile
    {
        // 缓存序列化选项，提高性能并保证配置一致
        // AllowNamedFloatingPointLiterals：兜底兼容历史数据中可能出现的 ±∞ / NaN（例如 Rect.Empty 的哨兵被写入文件）
        private static readonly JsonSerializerOptions _writeOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new AutomationStepBaseJsonConverter() }
        };

        private static readonly JsonSerializerOptions _readOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new AutomationStepBaseJsonConverter() }
        };

        private const string StepsJsonEntryName = "steps.json";

        /// <summary>
        /// 当前工作目录解析器（默认取主 ViewModel 的 StepImageWorkDir）。
        /// 单测可替换为固定目录，避免依赖 DI 容器。
        /// </summary>
        internal static Func<string> WorkDirResolver { get; set; } = () =>
        {
            try
            {
                return SingletonLocator.Main?.StepImageWorkDir;
            }
            catch
            {
                return null;
            }
        };

        #region AutoStep 压缩包格式

        /// <summary>
        /// 将步骤保存为 .autostep 压缩包（zip 格式）
        /// 包含 steps.json 和 images/ 目录下的裁剪图片；可选一并打包原图（供其他电脑重新编辑）
        ///
        /// 原子写入：先序列化到内存 → 写临时文件 → 成功后 File.Replace 替换原文件并保留 .bak。
        /// 任何一步失败都不会破坏已有工程文件。
        /// </summary>
        public static SavePackageResult SaveAsAutoStepPackage(ObservableCollection<AutomationStepBase> steps, string packagePath, bool includeSourceImages = false)
        {
            if (steps == null)
                throw new ArgumentNullException(nameof(steps), LanguageService.GetLocalizedString("Msg_StepsEmpty", "步骤列表不能为空"));
            if (string.IsNullOrWhiteSpace(packagePath))
                throw new ArgumentException(LanguageService.GetLocalizedString("Msg_PathEmpty", "文件路径不能为空"), nameof(packagePath));

            string targetPath = Path.GetFullPath(packagePath);
            string directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var result = new SavePackageResult { WorkDir = GetWorkDirPath(targetPath) };

            // 1. 先序列化到内存：序列化异常（如无限值、坏数据）在上一步就抛，旧文件完好无损
            string jsonString = JsonSerializer.Serialize(steps, _writeOptions);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);

            // 2. 写临时文件（与目标同目录，保证 File.Replace 同卷原子性）
            string tempPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
            var imagesToLand = new List<(string SourcePath, string EntryName)>();

            try
            {
                using (var zipStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    var jsonEntry = archive.CreateEntry(StepsJsonEntryName, CompressionLevel.Optimal);
                    using (var entryStream = jsonEntry.Open())
                    {
                        entryStream.Write(jsonBytes, 0, jsonBytes.Length);
                    }

                    // 收集并写入图片（可选含原图）
                    foreach (var step in steps)
                    {
                        CollectAndWriteImages(step, archive, includeSourceImages, imagesToLand, result);
                    }
                }

                // 3. 替换原文件：成功后才动目标文件；保留上一版 .bak
                if (File.Exists(targetPath))
                {
                    string backupPath = targetPath + ".bak";
                    File.Replace(tempPath, targetPath, backupPath, ignoreMetadataErrors: true);
                    result.BackupPath = backupPath;
                }
                else
                {
                    File.Move(tempPath, targetPath);
                }
            }
            catch
            {
                // 失败：清理临时文件，目标文件保持原样
                TryDeleteFile(tempPath);
                throw;
            }

            // 4. 图片落地到包对应的工作目录
            //    解决"另存为后 StepImageWorkDir 指向空目录，第二次保存图片全丢"的问题
            LandImagesToWorkDir(imagesToLand, result);

            return result;
        }

        /// <summary>
        /// 从 .autostep 压缩包加载步骤
        /// 先解压到临时目录并完成 JSON 校验，成功后替换工作目录；打开损坏包不会破坏当前图片。
        /// </summary>
        public static ObservableCollection<AutomationStepBase> LoadFromAutoStepPackage(string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
                throw new ArgumentException(LanguageService.GetLocalizedString("Msg_PathEmpty", "文件路径不能为空"), nameof(packagePath));

            string packageFullPath = Path.GetFullPath(packagePath);
            if (!File.Exists(packageFullPath))
                throw new FileNotFoundException(LanguageService.GetLocalizedString("Msg_FileNotFound", $"文件未找到: {packagePath}", packagePath), packagePath);

            // 解压到工作目录: {packagePath所在目录}/{文件名WithoutExt}_images/
            string dir = Path.GetDirectoryName(packageFullPath);
            string nameWithoutExt = Path.GetFileNameWithoutExtension(packageFullPath);
            string workDir = Path.Combine(dir, $"{nameWithoutExt}_images");
            string tempDir = Path.Combine(dir, $".{nameWithoutExt}_images_tmp_{Guid.NewGuid():N}");

            ObservableCollection<AutomationStepBase> steps;
            try
            {
                Directory.CreateDirectory(tempDir);
                ZipFile.ExtractToDirectory(packageFullPath, tempDir);

                // 在临时目录里完成读取与校验，失败时当前工作目录原样保留
                string jsonPath = Path.Combine(tempDir, StepsJsonEntryName);
                if (!File.Exists(jsonPath))
                    throw new InvalidOperationException(LanguageService.GetLocalizedString("Msg_PackageNoStepsJson", $"压缩包 '{packagePath}' 中未找到 steps.json。", packagePath));

                using (var stream = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    steps = JsonSerializer.Deserialize<ObservableCollection<AutomationStepBase>>(stream, _readOptions);
                }

                if (steps == null)
                    throw new InvalidOperationException(LanguageService.GetLocalizedString("Msg_PackageStepsJsonInvalid", $"压缩包 '{packagePath}' 中的 steps.json 内容为空或格式无效。", packagePath));

                // 后处理：解析旧格式 int 行号的 TrueGoto/FalseGoto 为 Uid
                ResolveLegacyGotoLineNumbers(steps);

                // 校验通过后再替换工作目录：先改名旧目录（同卷原子操作）再换入新目录
                if (Directory.Exists(workDir))
                {
                    string oldDir = workDir + ".old-" + Guid.NewGuid().ToString("N");
                    Directory.Move(workDir, oldDir);
                    try
                    {
                        Directory.Move(tempDir, workDir);
                    }
                    catch
                    {
                        // 换入失败：回滚旧目录，保证当前工程图片不丢
                        if (!Directory.Exists(workDir) && Directory.Exists(oldDir))
                            Directory.Move(oldDir, workDir);
                        throw;
                    }
                    TryDeleteDirectory(oldDir);
                }
                else
                {
                    Directory.Move(tempDir, workDir);
                }
            }
            catch
            {
                TryDeleteDirectory(tempDir);
                throw;
            }

            // 设置工作目录
            var mainVM = SingletonLocator.Main;
            if (mainVM != null)
            {
                mainVM.StepImageWorkDir = workDir;
                mainVM.StepFilePath = packageFullPath;
            }

            return steps;
        }

        /// <summary>
        /// 获取 .autostep 包的解压工作目录路径
        /// </summary>
        public static string GetWorkDirPath(string packagePath)
        {
            string fullPath = Path.GetFullPath(packagePath);
            string dir = Path.GetDirectoryName(fullPath);
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fullPath);
            return Path.Combine(dir, $"{nameWithoutExt}_images");
        }

        /// <summary>
        /// 收集步骤引用但工作目录中缺失的资源（图片 / 烧录模板），用于加载后显式告警。
        /// 返回可读的缺失清单；没有缺失时返回空列表。
        /// </summary>
        public static List<string> CollectMissingAssets(ObservableCollection<AutomationStepBase> steps, string workDir)
        {
            var missing = new List<string>();
            if (steps == null || string.IsNullOrEmpty(workDir)) return missing;

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step == null) continue;

                int lineNo = i + 1;
                string label = string.IsNullOrWhiteSpace(step.Name) ? $"#{lineNo}" : step.Name;

                if (step is ImageRecognitionBase imageStep)
                {
                    if (string.IsNullOrWhiteSpace(imageStep.CroppedImageName))
                    {
                        missing.Add(string.Format(LanguageService.GetLocalizedString("Missing_StepImage",
                            "第 {0} 行「{1}」未设置识别图片"), lineNo, label));
                    }
                    else
                    {
                        AddIfMissing(SafeCombine(workDir, imageStep.CroppedImageName), imageStep.CroppedImageName, missing);
                    }
                }
                else if (step is ImagesRecognitionBase imagesStep)
                {
                    if (imagesStep.Images == null) continue;
                    int index = 0;
                    foreach (var image in imagesStep.Images)
                    {
                        index++;
                        if (image == null) continue;
                        if (string.IsNullOrWhiteSpace(image.CroppedImageName))
                        {
                            missing.Add(string.Format(LanguageService.GetLocalizedString("Missing_StepMultiImage",
                                "第 {0} 行「{1}」第 {2} 张图片未设置"), lineNo, label, index));
                            continue;
                        }
                        AddIfMissing(SafeCombine(workDir, image.CroppedImageName), image.CroppedImageName, missing);
                    }
                }
                else if (step is GetInputStep inputStep)
                {
                    if (inputStep.TextPoints == null) continue;
                    int index = 0;
                    foreach (var point in inputStep.TextPoints)
                    {
                        index++;
                        if (point == null) continue;
                        if (string.IsNullOrWhiteSpace(point.CroppedImageName))
                        {
                            missing.Add(string.Format(LanguageService.GetLocalizedString("Missing_StepPointImage",
                                "第 {0} 行「{1}」第 {2} 个取点未设置图片"), lineNo, label, index));
                            continue;
                        }
                        AddIfMissing(SafeCombine(workDir, point.CroppedImageName), point.CroppedImageName, missing);
                    }
                }
                else if (step is BurnInConfigStep burnStep)
                {
                    foreach (var templateName in GetBurnInTemplateNames(burnStep))
                    {
                        AddIfMissing(SafeCombine(workDir, templateName), templateName, missing);
                    }
                }
            }

            return missing;
        }

        /// <summary>拼接工作目录与条目名；条目名为空（步骤没配图）时返回 null，避免 Path.Combine 的 path2 空引用。</summary>
        private static string SafeCombine(string dir, string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName)) return null;
            return Path.Combine(dir, NormalizeEntryName(entryName));
        }

        private static void AddIfMissing(string fullPath, string displayName, List<string> missing)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return;
            if (!File.Exists(fullPath)) missing.Add(displayName);
        }

        /// <summary>
        /// 收集步骤里的图片并写入压缩包，同时记录需要落地到工作目录的图片。
        /// </summary>
        private static void CollectAndWriteImages(AutomationStepBase step, ZipArchive archive, bool includeSourceImages,
            List<(string SourcePath, string EntryName)> imagesToLand, SavePackageResult result)
        {
            if (step is ImageRecognitionBase imageStep)
            {
                WriteCroppedImageToArchive(imageStep.CroppedImageFullPath, imageStep.CroppedImageName, archive, imagesToLand, result);
                // 可选打包原图：images/{Uid}_src{扩展名}，加载时按约定回退查找
                if (includeSourceImages && !string.IsNullOrEmpty(imageStep.ImagePath))
                    WriteCroppedImageToArchive(imageStep.ImagePath, $"images/{imageStep.Uid}_src{Path.GetExtension(imageStep.ImagePath)}", archive, imagesToLand, result);
            }
            else if (step is ImagesRecognitionBase imagesStep)
            {
                if (imagesStep.Images == null) return;
                foreach (var image in imagesStep.Images)
                {
                    if (image == null) continue;
                    WriteCroppedImageToArchive(image.CroppedImageFullPath, image.CroppedImageName, archive, imagesToLand, result);
                }
            }
            else if (step is GetInputStep inputStep)
            {
                // 获取输入步骤相对定位获取点的裁剪图（可选含原图）
                foreach (var point in inputStep.TextPoints)
                {
                    if (point == null) continue;
                    WriteCroppedImageToArchive(point.CroppedImageFullPath, point.CroppedImageName, archive, imagesToLand, result);
                    if (includeSourceImages && !string.IsNullOrEmpty(point.ImagePath))
                        WriteCroppedImageToArchive(point.ImagePath, $"images/{point.Id}_src{Path.GetExtension(point.ImagePath)}", archive, imagesToLand, result);
                }
            }
            else if (step is BurnInConfigStep burnStep)
            {
                // 烧录判据模板：GoodTemplateName/BadTemplateName/FailTemplateName（相对工作目录的 images/xxx.png）
                // 之前从不打包，导致换机或重开后模板缺失 → 三路命中全 false → 静默判烧录失败
                string sourceWorkDir = GetCurrentWorkDir();
                foreach (var templateName in GetBurnInTemplateNames(burnStep))
                {
                    string sourcePath = string.IsNullOrEmpty(sourceWorkDir)
                        ? null
                        : Path.Combine(sourceWorkDir, NormalizeEntryName(templateName));
                    WriteCroppedImageToArchive(sourcePath, templateName, archive, imagesToLand, result);
                }
            }
        }

        /// <summary>擦除烧录步骤的三个判据模板名（无则返回空）</summary>
        private static IEnumerable<string> GetBurnInTemplateNames(BurnInConfigStep step)
        {
            var names = new[] { step.GoodTemplateName, step.BadTemplateName, step.FailTemplateName };
            foreach (var name in names)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    yield return name;
            }
        }

        /// <summary>当前会话的工作目录（图片的运行时来源）</summary>
        private static string GetCurrentWorkDir()
        {
            try
            {
                return WorkDirResolver?.Invoke();
            }
            catch
            {
                return null;
            }
        }

        private static void WriteCroppedImageToArchive(string fullPath, string entryName, ZipArchive archive,
            List<(string SourcePath, string EntryName)> imagesToLand, SavePackageResult result)
        {
            if (string.IsNullOrEmpty(entryName))
                return;

            string normalizedEntryName = NormalizeEntryName(entryName);

            // 缺失图片不再静默跳过：记录到结果里，由 UI 明确告警
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                result.MissingImages.Add($"{normalizedEntryName} ← {fullPath ?? LanguageService.GetLocalizedString("Msg_UnknownPath", "(未知路径)")}");
                return;
            }

            var entry = archive.CreateEntry(normalizedEntryName, CompressionLevel.Optimal);
            using (var entryStream = entry.Open())
            using (var fileStream = File.OpenRead(fullPath))
            {
                fileStream.CopyTo(entryStream);
            }

            result.ImageCount++;
            imagesToLand.Add((fullPath, normalizedEntryName));
        }

        /// <summary>把已打包的图片复制到包对应的工作目录（原地保存时源与目标相同则跳过）</summary>
        private static void LandImagesToWorkDir(List<(string SourcePath, string EntryName)> imagesToLand, SavePackageResult result)
        {
            if (string.IsNullOrEmpty(result.WorkDir) || imagesToLand.Count == 0)
                return;

            try
            {
                Directory.CreateDirectory(result.WorkDir);
            }
            catch (Exception ex)
            {
                result.Warnings.Add(LanguageService.GetLocalizedString("Msg_CreateImageDirFailed", $"创建图片目录失败：{result.WorkDir}（{ex.Message}）", result.WorkDir, ex.Message));
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (sourcePath, entryName) in imagesToLand)
            {
                if (!seen.Add(entryName))
                    continue;

                try
                {
                    string target = Path.Combine(result.WorkDir, entryName.Replace('/', Path.DirectorySeparatorChar));
                    string targetFull = Path.GetFullPath(target);
                    if (string.Equals(Path.GetFullPath(sourcePath), targetFull, StringComparison.OrdinalIgnoreCase))
                        continue; // 原地保存：图片已在工作目录

                    string targetDir = Path.GetDirectoryName(targetFull);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                        Directory.CreateDirectory(targetDir);

                    File.Copy(sourcePath, targetFull, overwrite: true);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add(LanguageService.GetLocalizedString("Msg_ImageSaveFailed", $"图片落地失败：{entryName}（{ex.Message}）", entryName, ex.Message));
                }
            }
        }

        private static string NormalizeEntryName(string entryName)
        {
            return string.IsNullOrEmpty(entryName) ? entryName : entryName.Replace('\\', '/');
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 清理失败不影响主流程（临时文件残留可人工删除）
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }

        #endregion

        #region JSON 格式（兼容旧版）

        /// <summary>
        /// 将 AutomationStepBase 及其派生类列表保存为 JSON 文件
        /// </summary>
        [Obsolete("建议使用 SaveAsAutoStepPackage")]
        public static void SaveStepsToJson(ObservableCollection<AutomationStepBase> steps, string filePath)
        {
            if (steps == null)
                throw new ArgumentNullException(nameof(steps), LanguageService.GetLocalizedString("Msg_StepsEmpty", "步骤列表不能为空"));

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException(LanguageService.GetLocalizedString("Msg_PathEmpty", "文件路径不能为空"), nameof(filePath));

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string jsonString = JsonSerializer.Serialize(steps, _writeOptions);
            File.WriteAllText(filePath, jsonString, Encoding.UTF8);
        }

        /// <summary>
        /// 从 JSON 文件加载自动化步骤列表
        /// </summary>
        [Obsolete("建议使用 LoadFromAutoStepPackage")]
        public static ObservableCollection<AutomationStepBase> LoadStepsFromJson(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException(LanguageService.GetLocalizedString("Msg_PathEmpty", "文件路径不能为空"), nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException(LanguageService.GetLocalizedString("Msg_FileNotFound", $"文件未找到: {filePath}", filePath), filePath);

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var steps = JsonSerializer.Deserialize<ObservableCollection<AutomationStepBase>>(stream, _readOptions);

                if (steps == null)
                {
                    throw new InvalidOperationException(LanguageService.GetLocalizedString("Msg_JsonFileInvalid", $"文件 '{filePath}' 内容为空或格式无效，无法反序列化为步骤列表。", filePath));
                }

                // 后处理：解析旧格式 int 行号的 TrueGoto/FalseGoto 为 Uid
                ResolveLegacyGotoLineNumbers(steps);

                return steps;
            }
        }

        #endregion

        /// <summary>
        /// 解析旧格式（int 行号）的 TrueGoto/FalseGoto 为步骤 Uid
        /// </summary>
        private static void ResolveLegacyGotoLineNumbers(ObservableCollection<AutomationStepBase> steps)
        {
            var pending = AutomationStepBaseJsonConverter.TakePendingGotoResolution();
            if (pending == null || pending.IsEmpty) return;

            foreach (var kvp in pending)
            {
                var step = steps.FirstOrDefault(s => s.Uid == kvp.Key);
                if (step == null) continue;

                var (trueGotoLine, falseGotoLine) = kvp.Value;
                if (trueGotoLine > 0 && trueGotoLine <= steps.Count)
                    step.TrueGotoUid = steps[trueGotoLine - 1].Uid;
                if (falseGotoLine > 0 && falseGotoLine <= steps.Count)
                    step.FalseGotoUid = steps[falseGotoLine - 1].Uid;
            }
        }
    }
}
