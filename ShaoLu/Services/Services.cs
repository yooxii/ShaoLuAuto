using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UtfUnknown;

namespace ShaoLu.Services
{
    public class PathServices
    {
        public static string OpenPathDialog(string title = "Open File", string filter = "All File|*.*", string initPath = null, bool isDir = false)
        {
            OpenFileDialog dialog = new()
            {
                Title = title,
                Filter = filter,
                InitialDirectory = initPath
            };
            bool? result = dialog.ShowDialog();
            return result == true ? isDir ? Path.GetDirectoryName(dialog.FileName) : dialog.FileName : null;
        }

        public static string SavePathDialog(string title = "Open File", string filter = "All File|*.*", string saveName = "", string initPath = null)
        {
            SaveFileDialog dialog = new()
            {
                Title = title,
                FileName = saveName,
                Filter = filter,
                InitialDirectory = initPath
            };
            bool? result = dialog.ShowDialog();
            return result == true ? dialog.FileName : null;
        }

        public static string GetRelativePath(string absolutePath, string relativeTo)
        {
            string[] absoluteDirs = absolutePath.Split(['\\', '/']);
            string[] relativeDirs = relativeTo.Split(['\\', '/']);

            int length = Math.Min(absoluteDirs.Length, relativeDirs.Length);
            int lastCommonRoot = -1;
            for (int i = 0; i < length; i++)
            {
                if (absoluteDirs[i] == relativeDirs[i])
                    lastCommonRoot = i;
                else
                    break;
            }

            if (lastCommonRoot == -1)
                throw new ArgumentException("Paths do not have a common base");

            StringBuilder sb = new();
            for(int i = lastCommonRoot + 1; i < absoluteDirs.Length; i++)
            {
                if (absoluteDirs[i].Length > 0)
                    sb.Append("..\\");
            }
            for (int i = lastCommonRoot + 1; i < relativeDirs.Length; i++)
            {
                sb.Append(relativeDirs[i]);
                if (i < relativeDirs.Length - 1)
                    sb.Append("\\");
            }
            return sb.ToString();
        }
    }

    public class FileServices
    {
        public List<string> ReadyToDeleteFiles = [];


        /// <summary>
        /// 标记文件为待删除。
        /// </summary>
        public void MarkForDeletion(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            // 使用 HashSet 避免重复添加
            if (!ReadyToDeleteFiles.Contains(filePath))
            {
                ReadyToDeleteFiles.Add(filePath);
                System.Diagnostics.Debug.WriteLine($"[GC] Marked for deletion: {filePath}");
            }
        }

        /// <summary>
        /// 取消标记删除。
        /// 用于撤销“删除步骤”或“替换裁剪图”的操作。
        /// </summary>
        public void UnmarkForDeletion(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            if (ReadyToDeleteFiles.Remove(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"[Undo] Unmarked for deletion: {filePath}");
            }
        }

        public void CommitPendingDeletions()
        {
            foreach (var filePath in ReadyToDeleteFiles) // ToList 避免枚举时修改集合
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        System.Diagnostics.Debug.WriteLine($"[Clean] Deleted: {filePath}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Clean] Failed to delete {filePath}: {ex.Message}");
                }
            }
            ReadyToDeleteFiles.Clear();
        }

        public string SmartReadTextFile(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            if (bytes.Length == 0) return string.Empty;

            // 1. 优先检查 BOM（最准确）
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes); // UTF-16 LE
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes); // UTF-16 BE

            // 2. 无 BOM 时，使用 UtfUnknown 进行启发式检测
            var result = CharsetDetector.DetectFromBytes(bytes);

            // 置信度阈值建议设为 0.5，比 Ude 的 0.3 更严格，减少误判
            if (result.Detected != null && result.Detected.Confidence > 0.5)
            {
                try
                {
                    // UtfUnknown 返回的 Charset 名称与 .NET Encoding 兼容
                    return Encoding.GetEncoding(result.Detected.EncodingName).GetString(bytes);
                }
                catch
                {
                    throw new Exception("");
                }
            }
            else
            {
                throw new Exception("");
            }
        }

    }
}
