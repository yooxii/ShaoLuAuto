using ShaoLu.Converters;
using ShaoLu.Viewmodels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShaoLu.Utils
{
    public class StepsFile
    {
        // 缓存序列化选项，提高性能并保证配置一致
        private static readonly JsonSerializerOptions _writeOptions = new JsonSerializerOptions
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
            // 注意：如果 AutomationStepBase 有派生类，必须在此注册自定义 Converter
            Converters = { new AutomationStepBaseJsonConverter() }
        };

        /// <summary>
        /// 将 AutomationStepBase 及其派生类列表保存为 JSON 文件
        /// </summary>
        public static void SaveStepsToJson(ObservableCollection<AutomationStepBase> steps, string filePath)
        {
            if (steps == null)
                throw new ArgumentNullException(nameof(steps), "步骤列表不能为空");

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("文件路径不能为空", nameof(filePath));

            // 确保目录存在
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 直接序列化并写入，让可能的 IOException, UnauthorizedAccessException, JsonException 自然抛出
            // 调用方应在 ViewModel 或全局异常处理器中捕获这些异常
            string jsonString = JsonSerializer.Serialize(steps, _writeOptions);
            File.WriteAllText(filePath, jsonString, Encoding.UTF8);
        }

        /// <summary>
        /// 从 JSON 文件加载自动化步骤列表
        /// </summary>
        public static ObservableCollection<AutomationStepBase> LoadStepsFromJson(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("文件路径不能为空", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"文件未找到: {filePath}", filePath);

            // 直接反序列化，让可能的 IOException, UnauthorizedAccessException, JsonException 自然抛出
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var steps = JsonSerializer.Deserialize<ObservableCollection<AutomationStepBase>>(stream, _readOptions);

                if (steps == null)
                {
                    // 仅在结果为 null 时抛出明确的业务/格式错误
                    throw new InvalidOperationException($"文件 '{filePath}' 内容为空或格式无效，无法反序列化为步骤列表。");
                }

                return steps;
            }
        }
    }
}