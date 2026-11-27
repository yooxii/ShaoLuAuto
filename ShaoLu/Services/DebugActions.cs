using ShaoLu.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ShaoLu.Services
{
    /// <summary>
    /// 调试菜单 / 调试工具栏共用的小动作：打开调试日志文件夹、打开最近日志、清空、截图。
    /// 提示信息都在这里统一给出，菜单与工具栏的行为保持一致。
    /// </summary>
    public static class DebugActions
    {
        /// <summary>打开调试日志文件夹（logs\debug）</summary>
        public static void OpenLogFolder()
        {
            try
            {
                Directory.CreateDirectory(DebugLogService.LogDirectory);
                OpenInShell(DebugLogService.LogDirectory);
            }
            catch (Exception ex)
            {
                Show(string.Format(LanguageService.GetLocalizedString("Debug_OpenFailed", "打开失败：{0}"), ex.Message));
            }
        }

        /// <summary>用默认程序打开最近一份调试日志</summary>
        public static void OpenLatestLog()
        {
            try
            {
                string path = DebugLogService.LatestLogPath();
                if (string.IsNullOrEmpty(path))
                {
                    Show(LanguageService.GetLocalizedString("Debug_NoLog", "还没有调试日志：先勾选「调试模式」，再运行一次流程。"));
                    return;
                }

                OpenInShell(path);
            }
            catch (Exception ex)
            {
                Show(string.Format(LanguageService.GetLocalizedString("Debug_OpenFailed", "打开失败：{0}"), ex.Message));
            }
        }

        /// <summary>清空调试日志（先确认）</summary>
        public static void ClearLogs()
        {
            try
            {
                var answer = WPFDevelopers.Controls.MessageBox.Show(
                    LanguageService.GetLocalizedString("Debug_ClearConfirm", "确定删除全部调试日志（含调试截图）吗？"),
                    LanguageService.GetLocalizedString("Debug_Clear", "清空调试日志"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;

                int count = DebugLogService.ClearLogs();
                Show(string.Format(LanguageService.GetLocalizedString("Debug_Cleared", "已清理 {0} 个调试日志文件。"), count));
            }
            catch (Exception ex)
            {
                Show(ex.Message);
            }
        }

        /// <summary>当前屏幕截图存进调试日志目录</summary>
        public static void Screenshot()
        {
            try
            {
                string path = DebugLogService.SaveScreenshot();
                if (string.IsNullOrEmpty(path))
                {
                    Show(LanguageService.GetLocalizedString("Debug_ScreenshotFailed", "截图失败，请检查屏幕权限。"));
                    return;
                }

                Show(string.Format(LanguageService.GetLocalizedString("Debug_ScreenshotSaved", "截图已保存：{0}"), path));
            }
            catch (Exception ex)
            {
                Show(ex.Message);
            }
        }

        private static void OpenInShell(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch
            {
                // 双击打开失败时退回资源管理器定位
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
        }

        private static void Show(string message)
        {
            WPFDevelopers.Controls.MessageBox.Show(message,
                LanguageService.GetLocalizedString("Menu_Debug", "调试"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
