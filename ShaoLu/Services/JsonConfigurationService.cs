using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography; // 用于加密敏感数据
using System.Text.Json;
using System.Threading.Tasks;

namespace ShaoLu.Services
{
    public class JsonConfigurationService : IConfigurationService
    {
        private readonly string _configPath;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public JsonConfigurationService()
        {
            // 配置文件保存在 %AppData%\AutoShaoLu\settings.json
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "AutoShaoLu");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            _configPath = Path.Combine(dir, "settings.json");

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<T> GetSettingAsync<T>(string key, T defaultValue = default) where T : class
        {
            if (!File.Exists(_configPath)) return defaultValue;

            try
            {
                //var json = await File.ReadAllTextAsync(_configPath);
                var json = File.ReadAllText(_configPath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty(key, out var element))
                {
                    return element.Deserialize<T>(_jsonOptions);
                }
            }
            catch (Exception ex)
            {
                // 记录日志，返回默认值
                logger.Error(ex, "Failed to read setting: {Key}", key);
            }

            return defaultValue;
        }

        public async Task SaveSettingAsync<T>(string key, T value) where T : class
        {
            // 1. 读取现有配置
            var settings = new Dictionary<string, object>();
            if (File.Exists(_configPath))
            {
                try
                {
                    //var json = await File.ReadAllTextAsync(_configPath);
                    var json = File.ReadAllText(_configPath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions) ?? [];
                }
                catch { /* 文件损坏则新建 */ }
            }

            // 2. 更新键值
            settings[key] = value;

            // 3. 写回文件
            var newJson = JsonSerializer.Serialize(settings, _jsonOptions);
            //await File.WriteAllTextAsync(_configPath, newJson);
            File.WriteAllText(_configPath, newJson);
        }

        public Task RemoveSettingAsync(string key)
        {
            // 实现略：读取->移除Key->写入
            throw new NotImplementedException();
        }
    }
}