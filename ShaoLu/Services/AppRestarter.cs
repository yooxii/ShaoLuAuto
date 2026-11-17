using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ShaoLu.Services
{
    /// <summary>
    /// 重启当前程序：启动一个新的自身进程后关闭当前进程。
    /// 用于「装好扩展后直接重载」这类必须重新初始化才能生效的场景（OCR 扩展在启动时才加载）。
    /// </summary>
    public static class AppRestarter
    {
        /// <summary>
        /// 启动新的自身进程并关闭当前程序。
        /// 返回 false 表示新进程没能起来（此时不关闭当前程序，避免用户丢掉整个程序）。
        /// </summary>
        public static bool TryRestart(out string error)
        {
            error = null;
            try
            {
                string exePath = GetExecutablePath();
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    error = LanguageService.GetLocalizedString("Ext_RestartExeMissing", "找不到当前程序的可执行文件。");
                    return false;
                }

                var startInfo = new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = AppContext.BaseDirectory,
                };

                Process.Start(startInfo);

                // 关窗前要绕过「未保存修改」的确认（此时已经由调用方确认过），由调用方设置对应状态
                Application.Current?.Shutdown();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string GetExecutablePath()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }
    }
}
