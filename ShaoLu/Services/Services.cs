using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;

namespace ShaoLu.Services
{
    public class PathServices
    {
        public string OpenPathDialog(string title = "Open File", string filter = "All File|*.*", string initPath = null, bool isDir = false)
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

        public string SavePathDialog(string title = "Open File", string filter = "All File|*.*", string saveName = "", string initPath = null)
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
    }
}
