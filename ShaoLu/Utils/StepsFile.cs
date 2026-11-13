using ShaoLu.Converters;
using ShaoLu.Models;
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
        /// 包含 steps.json、images/ 目录下的裁剪图片，并默认一并打包原图（原图按「时间字符串 + 编号」命名，
        /// 供其他电脑重新编辑图片）。
        ///
        /// 原子写入：先序列化到内存 → 写临时文件 → 成功后 File.Replace 替换原文件并保留 .bak。
        /// 任何一步失败都不会破坏已有工程文件。
        /// </summary>
        public static SavePackageResult SaveAsAutoStepPackage(ObservableCollection<AutomationStepBase> steps, string packagePath, bool includeSourceImages = true)
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

            // 0. 原图先定好包内名称（时间字符串 + 编号）：必须在序列化之前，名字才会写进 steps.json
            if (includeSourceImages)
                PrepareSourceImageNames(steps);

            // 0.1 裁剪图条目名也要唯一：复制/粘贴图像步骤会让两条步骤共用同一个图片名，
            //     同一条目名在 zip 里出现两次，工程解压会直接失败（重新打开时报图片不存在）
            PrepareCroppedImageNames(steps);

            // 1. 先序列化到内存：序列化异常（如无限值、坏数据）在上一步就抛，旧文件完好无损
            string jsonString = JsonSerializer.Serialize(steps, _writeOptions);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);

            // 2. 写临时文件（与目标同目录，保证 File.Replace 同卷原子性）
            string tempPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
            var imagesToLand = new List<(string SourcePath, string EntryName)>();
            // 已写入包的条目名：zip 里同名条目出现两次会让解压失败，这里做最后一道兜底
            var writtenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                        CollectAndWriteImages(step, archive, includeSourceImages, imagesToLand, writtenEntries, result);
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

            // 5. 原图路径改指向工程目录里的副本：临时目录被系统清理后仍能继续编辑
            if (includeSourceImages)
                RelinkSourceImages(steps, result.WorkDir);

            return result;
        }

        /// <summary>
        /// 给还没有包内名称的原图按「时间字符串 + 编号」生成名称，并消除重名
        /// （步骤复制/粘贴会让多个步骤挂着同一个原图名称，同一条目名在 zip 里出现两次会导致解压失败）。
        /// 必须在序列化 steps.json 之前调用，名字才会随工程一起保存。
        /// </summary>
        private static void PrepareSourceImageNames(ObservableCollection<AutomationStepBase> steps)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new List<ImageRecognitionBase>();

            // 第一遍：登记已有名称，重复的清空后重新生成
            foreach (var step in steps)
            {
                if (step is not ImageRecognitionBase imageStep)
                    continue;

                // 只有确实能找到原图才需要名称（只有裁剪图的步骤不打包原图）
                string sourcePath = ResolveSourceImagePath(imageStep);
                if (string.IsNullOrWhiteSpace(sourcePath))
                    continue;

                pending.Add(imageStep);

                string name = imageStep.SourceImageName;
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (!used.Add(NormalizeEntryName(name)))
                    imageStep.SourceImageName = null;
            }

            // 第二遍：补齐名称
            foreach (var imageStep in pending)
            {
                if (!string.IsNullOrWhiteSpace(imageStep.SourceImageName))
                    continue;

                string sourcePath = ResolveSourceImagePath(imageStep);

                // 原图本来就在工程目录 images/ 里（老工程、上次已落地）：沿用文件原名，不另起新名
                string existingName = GetWorkDirImageName(sourcePath);
                if (!string.IsNullOrWhiteSpace(existingName) && used.Add(existingName))
                {
                    imageStep.SourceImageName = existingName;
                    continue;
                }

                string extension = Path.GetExtension(sourcePath);
                if (string.IsNullOrWhiteSpace(extension))
                    extension = StepSourceImageStore.DefaultExtension;
                imageStep.SourceImageName = StepSourceImageStore.CreatePackageEntryName(used, extension);
            }
        }

        /// <summary>
        /// 原图位于当前工作目录 images/ 下时，返回它的相对条目名（images/xxx.png），否则返回 null
        /// </summary>
        private static string GetWorkDirImageName(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return null;

            try
            {
                string workDir = GetCurrentWorkDir();
                if (string.IsNullOrEmpty(workDir))
                    return null;

                string root = Path.GetFullPath(workDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(fullPath);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return null;

                string relative = full.Substring(root.Length).Replace('\\', '/');
                return relative.StartsWith("images/", StringComparison.OrdinalIgnoreCase) ? relative : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 已打包原图落地后，把步骤里的原图路径改指向工程目录里的副本。
        /// 临时目录里的原图会被系统清理，工程目录里的副本才是稳定来源。
        /// </summary>
        private static void RelinkSourceImages(ObservableCollection<AutomationStepBase> steps, string workDir)        {
            if (steps == null || string.IsNullOrEmpty(workDir))
                return;

            foreach (var step in steps)
            {
                if (step is not ImageRecognitionBase imageStep || string.IsNullOrWhiteSpace(imageStep.SourceImageName))
                    continue;

                string landed = Path.Combine(workDir, NormalizeEntryName(imageStep.SourceImageName).Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(landed))
                    continue;

                bool alreadyLanded;
                try
                {
                    alreadyLanded = !string.IsNullOrEmpty(imageStep.ImagePath)
                        && Path.GetFullPath(landed).Equals(Path.GetFullPath(imageStep.ImagePath), StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    alreadyLanded = false;
                }

                if (!alreadyLanded)
                    imageStep.ImagePath = landed;
            }
        }

        /// <summary>
        /// 裁剪图条目名唯一化：复制/粘贴图像步骤时，克隆体继承原步骤的图片名，两条步骤抢同一个文件。
        /// 同一条目名在 zip 里出现两次会让工程解压失败（重新打开时报图片不存在），
        /// 因此序列化前把重名的裁剪图改成「以自身 Uid 命名」的新条目，并把图片文件复制到新条目名下，
        /// 让每条步骤都有自己的图片，之后各自保存、删除互不影响。
        /// </summary>
        private static void PrepareCroppedImageNames(ObservableCollection<AutomationStepBase> steps)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var owner in CollectCroppedImageOwners(steps))
            {
                string name = owner.GetEntryName();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                string normalized = NormalizeEntryName(name);
                if (used.Add(normalized))
                {
                    if (!string.Equals(normalized, name, StringComparison.Ordinal))
                        owner.SetEntryName(normalized);
                    continue;
                }

                // 重名：图片文件还在时复制成自己的条目名；文件本来就不存在时不用改名（没有条目可写）
                string sourcePath = owner.GetFullPath();
                if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                    continue;

                string extension = Path.GetExtension(normalized);
                if (string.IsNullOrWhiteSpace(extension))
                    extension = StepSourceImageStore.DefaultExtension;

                string newEntry = CreateUniqueCroppedEntryName(used, owner.Uid, extension);
                if (string.IsNullOrEmpty(newEntry))
                    continue;

                try
                {
                    string directory = Path.GetDirectoryName(sourcePath);
                    string target = Path.Combine(directory ?? string.Empty, Path.GetFileName(newEntry));
                    if (!File.Exists(target))
                        File.Copy(sourcePath, target, overwrite: false);

                    owner.SetEntryName(newEntry);
                }
                catch (Exception ex)
                {
                    // 复制失败时保留原名：写入环节会跳过同名条目，两条步骤仍共用包内那一张图（不会毁掉工程）
                    NLog.LogManager.GetCurrentClassLogger()
                        .Warn(ex, "PrepareCroppedImageNames: copy {0} -> {1} failed", sourcePath, newEntry);
                }
            }
        }

        /// <summary>
        /// 列出一条工程里所有的裁剪图引用（单图步骤 / 多图步骤的每张图 / 获取文本步骤的相对定位取点）
        /// </summary>
        private static List<CroppedImageOwner> CollectCroppedImageOwners(ObservableCollection<AutomationStepBase> steps)
        {
            var owners = new List<CroppedImageOwner>();
            if (steps == null) return owners;

            foreach (var step in steps)
            {
                if (step == null) continue;

                if (step is ImageRecognitionBase imageStep)
                {
                    // 闭包捕获必须用局部变量，否则所有委托都会指向循环最后一项
                    var owner = imageStep;
                    owners.Add(new CroppedImageOwner
                    {
                        Uid = owner.Uid,
                        GetEntryName = () => owner.CroppedImageName,
                        SetEntryName = name => owner.CroppedImageName = name,
                        GetFullPath = () => owner.CroppedImageFullPath,
                    });
                }
                else if (step is ImagesRecognitionBase imagesStep)
                {
                    if (imagesStep.Images == null) continue;
                    foreach (var image in imagesStep.Images)
                    {
                        if (image == null) continue;
                        var owner = image;
                        owners.Add(new CroppedImageOwner
                        {
                            Uid = owner.Uid,
                            GetEntryName = () => owner.CroppedImageName,
                            SetEntryName = name => owner.CroppedImageName = name,
                            GetFullPath = () => owner.CroppedImageFullPath,
                        });
                    }
                }
                else if (step is GetInputStep inputStep)
                {
                    if (inputStep.TextPoints == null) continue;
                    foreach (var point in inputStep.TextPoints)
                    {
                        // 只有「图像相对定位」的取点才有裁剪图，其余取点直接读屏幕
                        if (point == null || point.Source != TextPointSource.Relative) continue;
                        var owner = point;
                        owners.Add(new CroppedImageOwner
                        {
                            Uid = owner.Id,
                            GetEntryName = () => owner.CroppedImageName,
                            SetEntryName = name => owner.CroppedImageName = name,
                            GetFullPath = () => owner.CroppedImageFullPath,
                        });
                    }
                }
            }

            return owners;
        }

        /// <summary>生成一个未被占用的裁剪图条目名：images/{Uid}{扩展名}，冲突时追加序号</summary>
        private static string CreateUniqueCroppedEntryName(ISet<string> used, Guid uid, string extension)
        {
            string baseName = $"{StepSourceImageStore.PackageFolder}/{uid}";
            for (int index = 1; index <= 1000; index++)
            {
                string candidate = index == 1 ? baseName + extension : $"{baseName}_{index}{extension}";
                if (used.Add(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>一条裁剪图引用：谁在引用、当前条目名、怎么改名</summary>
        private sealed class CroppedImageOwner
        {
            public Guid Uid { get; set; }
            public Func<string> GetEntryName { get; set; }
            public Action<string> SetEntryName { get; set; }
            public Func<string> GetFullPath { get; set; }
        }

        /// <summary>
        /// 把工程包解压到指定目录。
        /// 不用 ZipFile.ExtractToDirectory：它遇到「同名条目已存在」会直接失败，
        /// 而旧版本保存的工程包可能含有同名图片条目（复制步骤共用图片名）。
        /// 这里逐条目解压、同名以最后一次为准，让这类旧包仍能打开，重新保存一次就能修好。
        /// </summary>
        private static void ExtractPackage(string packagePath, string targetDir)
        {
            string root = Path.GetFullPath(targetDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            using var archive = ZipFile.OpenRead(packagePath);
            foreach (var entry in archive.Entries)
            {
                // 目录条目：文件条目会按需创建目录，这里跳过
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                string destination = Path.GetFullPath(Path.Combine(
                    targetDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));

                // 防目录穿越：条目名里带 .. 时不能写到临时目录外面
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"工程包内条目路径非法：{entry.FullName}");

                string directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                entry.ExtractToFile(destination, overwrite: true);
            }
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
                ExtractPackage(packageFullPath, tempDir);

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

                        // 只有「图像相对定位」的取点才需要图片；
                        // 屏幕绝对坐标 / 屏幕相对坐标运行时直接读屏幕，没有图片是正常的，不能报缺失
                        if (point.Source != TextPointSource.Relative)
                            continue;

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
        /// <paramref name="writtenEntries"/> 记录已经写过的条目名，同名条目只写一次
        /// （重复条目会让 zip 解压失败，工程重新打开时报错）。
        /// </summary>
        private static void CollectAndWriteImages(AutomationStepBase step, ZipArchive archive, bool includeSourceImages,
            List<(string SourcePath, string EntryName)> imagesToLand, ISet<string> writtenEntries, SavePackageResult result)
        {
            if (step is ImageRecognitionBase imageStep)
            {
                WriteCroppedImageToArchive(imageStep.CroppedImageFullPath, imageStep.CroppedImageName, archive, imagesToLand, writtenEntries, result);
                // 原图：默认按「时间字符串 + 编号」的包内名称打包（旧数据没有名称时回退 images/{Uid}_src{扩展名}）
                if (includeSourceImages)
                {
                    string sourcePath = ResolveSourceImagePath(imageStep);
                    if (!string.IsNullOrEmpty(sourcePath))
                    {
                        string extension = Path.GetExtension(sourcePath);
                        if (string.IsNullOrWhiteSpace(extension))
                            extension = StepSourceImageStore.DefaultExtension;
                        string entryName = string.IsNullOrWhiteSpace(imageStep.SourceImageName)
                            ? $"images/{imageStep.Uid}_src{extension}"
                            : imageStep.SourceImageName;
                        WriteCroppedImageToArchive(sourcePath, entryName, archive, imagesToLand, writtenEntries, result);
                    }
                }
            }
            else if (step is ImagesRecognitionBase imagesStep)
            {
                if (imagesStep.Images == null) return;
                foreach (var image in imagesStep.Images)
                {
                    if (image == null) continue;
                    WriteCroppedImageToArchive(image.CroppedImageFullPath, image.CroppedImageName, archive, imagesToLand, writtenEntries, result);
                }
            }
            else if (step is GetInputStep inputStep)
            {
                // 获取输入步骤相对定位获取点的裁剪图（可选含原图）
                foreach (var point in inputStep.TextPoints)
                {
                    if (point == null) continue;
                    WriteCroppedImageToArchive(point.CroppedImageFullPath, point.CroppedImageName, archive, imagesToLand, writtenEntries, result);
                    if (includeSourceImages && !string.IsNullOrEmpty(point.ImagePath))
                        WriteCroppedImageToArchive(point.ImagePath, $"images/{point.Id}_src{Path.GetExtension(point.ImagePath)}", archive, imagesToLand, writtenEntries, result);
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
                    WriteCroppedImageToArchive(sourcePath, templateName, archive, imagesToLand, writtenEntries, result);
                }
            }
        }

        /// <summary>
        /// 取步骤当前可用的原图路径（优先步骤里记录的原图路径，其次工程目录里的原图）。
        /// 单测环境可能没有 DI 容器，解析失败时退回原始路径，不抛异常。
        /// </summary>
        private static string ResolveSourceImagePath(ImageRecognitionBase imageStep)
        {
            try
            {
                return imageStep.SourceImageFullPath;
            }
            catch
            {
                return imageStep.ImagePath;
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
            List<(string SourcePath, string EntryName)> imagesToLand, ISet<string> writtenEntries, SavePackageResult result)
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

            // 同一条目名只写一次（重名的两条步骤共用同一张图片，解压后都还能用）；
            // zip 里出现同名条目会让整个工程解压失败，所以这里必须挡住
            if (writtenEntries != null && !writtenEntries.Add(normalizedEntryName))
                return;

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
