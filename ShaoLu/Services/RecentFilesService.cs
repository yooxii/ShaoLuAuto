using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ShaoLu.Services
{
    /// <summary>
    /// 最近打开文件列表服务（持久化到 AppData\AutoShaoLu\recent_files.json，最多保留 10 条）
    /// </summary>
    public static class RecentFilesService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>最多保留的最近文件数量</summary>
        public const int MaxCount = 10;

        private static string ListPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoShaoLu", "recent_files.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        /// <summary>加载最近打开文件列表（按最近使用排序）</summary>
        public static List<string> Load()
        {
            try
            {
                if (!File.Exists(ListPath)) return new List<string>();
                return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ListPath)) ?? new List<string>();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Load recent files failed");
                return new List<string>();
            }
        }

        /// <summary>记录一次成功打开的文件（去重并置顶，超出上限时裁剪）</summary>
        public static void AddRecent(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var list = Load();
                list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                list.Insert(0, path);
                Save(list.Take(MaxCount).ToList());
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Save recent files failed");
            }
        }

        /// <summary>从列表中移除指定文件（如文件已不存在）</summary>
        public static void Remove(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var list = Load();
                if (list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
                    Save(list);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Remove recent file failed");
            }
        }

        private static void Save(List<string> list)
        {
            string dir = Path.GetDirectoryName(ListPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(ListPath, JsonSerializer.Serialize(list, JsonOptions));
        }
    }
}
