using ShaoLu.Converters;
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
    public class StepsFile
    {
        // 缓存序列化选项，提高性能并保证配置一致
        private static readonly JsonSerializerOptions _writeOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new AutomationStepBaseJsonConverter() }
        };

        private static readonly JsonSerializerOptions _readOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new AutomationStepBaseJsonConverter() }
        };

        #region AutoStep 压缩包格式

        /// <summary>
        /// 将步骤保存为 .autostep 压缩包（zip 格式）
        /// 包含 steps.json 和 images/ 目录下的裁剪图片
        /// </summary>
        public static void SaveAsAutoStepPackage(ObservableCollection<AutomationStepBase> steps, string packagePath)
        {
            if (steps == null)
                throw new ArgumentNullException(nameof(steps), "步骤列表不能为空");
            if (string.IsNullOrWhiteSpace(packagePath))
                throw new ArgumentException("文件路径不能为空", nameof(packagePath));

            string directory = Path.GetDirectoryName(packagePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // 如果文件已存在，先删除
            if (File.Exists(packagePath))
                File.Delete(packagePath);

            using var zipStream = new FileStream(packagePath, FileMode.Create);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
            // 1. 写入 steps.json
            var jsonEntry = archive.CreateEntry("steps.json", CompressionLevel.Optimal);
            using (var entryStream = jsonEntry.Open())
            {
                string jsonString = JsonSerializer.Serialize(steps, _writeOptions);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);
                entryStream.Write(jsonBytes, 0, jsonBytes.Length);
            }

            // 2. 收集并写入裁剪图片
            foreach (var step in steps)
            {
                CollectAndWriteImages(step, archive);
            }
        }

        /// <summary>
        /// 从 .autostep 压缩包加载步骤
        /// </summary>
        public static ObservableCollection<AutomationStepBase> LoadFromAutoStepPackage(string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
                throw new ArgumentException("文件路径不能为空", nameof(packagePath));
            if (!File.Exists(packagePath))
                throw new FileNotFoundException($"文件未找到: {packagePath}", packagePath);

            // 解压到工作目录: {packagePath所在目录}/{文件名WithoutExt}_images/
            string dir = Path.GetDirectoryName(packagePath);
            string nameWithoutExt = Path.GetFileNameWithoutExtension(packagePath);
            string workDir = Path.Combine(dir, $"{nameWithoutExt}_images");

            // 清理旧的工作目录
            if (Directory.Exists(workDir))
                Directory.Delete(workDir, true);
            Directory.CreateDirectory(workDir);

            // 解压 zip 到工作目录
            ZipFile.ExtractToDirectory(packagePath, workDir);

            // 读取 steps.json
            string jsonPath = Path.Combine(workDir, "steps.json");
            if (!File.Exists(jsonPath))
                throw new InvalidOperationException($"压缩包 '{packagePath}' 中未找到 steps.json。");

            ObservableCollection<AutomationStepBase> steps;
            using (var stream = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                steps = JsonSerializer.Deserialize<ObservableCollection<AutomationStepBase>>(stream, _readOptions);
            }

            if (steps == null)
                throw new InvalidOperationException($"压缩包 '{packagePath}' 中的 steps.json 内容为空或格式无效。");

            // 后处理：解析旧格式 int 行号的 TrueGoto/FalseGoto 为 Uid
            ResolveLegacyGotoLineNumbers(steps);

            // 设置工作目录
            var mainVM = SingletonLocator.Main;
            mainVM.StepImageWorkDir = workDir;
            mainVM.StepFilePath = packagePath;

            return steps;
        }

        /// <summary>
        /// 获取 .autostep 包的解压工作目录路径
        /// </summary>
        public static string GetWorkDirPath(string packagePath)
        {
            string dir = Path.GetDirectoryName(packagePath);
            string nameWithoutExt = Path.GetFileNameWithoutExtension(packagePath);
            return Path.Combine(dir, $"{nameWithoutExt}_images");
        }

        private static void CollectAndWriteImages(AutomationStepBase step, ZipArchive archive)
        {
            if (step is ImageRecognitionBase imageStep)
            {
                if (!string.IsNullOrEmpty(imageStep.CroppedImageFullPath) && !string.IsNullOrEmpty(imageStep.CroppedImageName))
                    WriteCroppedImageToArchive(imageStep.CroppedImageFullPath, imageStep.CroppedImageName, archive);
            }
            else if (step is ImagesRecognitionBase imagesStep)
            {
                if (imagesStep.Images == null) return;
                foreach (var image in imagesStep.Images)
                {
                    if (image == null) continue;
                    if (!string.IsNullOrEmpty(image.CroppedImageFullPath) && !string.IsNullOrEmpty(image.CroppedImageName))
                        WriteCroppedImageToArchive(image.CroppedImageFullPath, image.CroppedImageName, archive);
                }
            }
        }

        private static void WriteCroppedImageToArchive(string fullPath, string entryName, ZipArchive archive)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(entryName))
                return;
            if (!File.Exists(fullPath))
                return;

            // 使用正斜杠作为 zip 内路径分隔符
            string normalizedEntryName = entryName.Replace('\\', '/');
            var entry = archive.CreateEntry(normalizedEntryName, CompressionLevel.Optimal);
            using (var entryStream = entry.Open())
            using (var fileStream = File.OpenRead(fullPath))
            {
                fileStream.CopyTo(entryStream);
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
                throw new ArgumentNullException(nameof(steps), "步骤列表不能为空");

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("文件路径不能为空", nameof(filePath));

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
                throw new ArgumentException("文件路径不能为空", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"文件未找到: {filePath}", filePath);

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var steps = JsonSerializer.Deserialize<ObservableCollection<AutomationStepBase>>(stream, _readOptions);

                if (steps == null)
                {
                    throw new InvalidOperationException($"文件 '{filePath}' 内容为空或格式无效，无法反序列化为步骤列表。");
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