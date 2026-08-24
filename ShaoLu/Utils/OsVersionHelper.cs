using System;
using System.Runtime.InteropServices;

namespace ShaoLu.Utils
{
    /// <summary>
    /// 操作系统版本检测（供选择文档查看方式等场景使用）
    /// 注意：.NET Framework 下 Environment.OSVersion 在 Win8.1+ 会因兼容性 shim 返回 6.2，
    /// 必须使用 RtlGetVersion 获取真实版本号
    /// </summary>
    public static class OsVersionHelper
    {
        /// <summary>是否 Windows 10 及以上（含 Win11）</summary>
        public static readonly bool IsWindows10OrLater = DetectWindows10OrLater();

        /// <summary>是否已安装 WebView2 Runtime</summary>
        public static readonly bool IsWebView2Installed = DetectWebView2();

        /// <summary>完整版本文本，如 "Windows 10 (10.0.19045)"</summary>
        public static readonly string VersionText = BuildVersionText();

        private static bool DetectWindows10OrLater()
        {
            var info = new NativeMethods.OSVERSIONINFOEX
            {
                dwOSVersionInfoSize = Marshal.SizeOf(typeof(NativeMethods.OSVERSIONINFOEX)),
            };
            try
            {
                if (NativeMethods.RtlGetVersion(ref info) == 0)
                    return info.dwMajorVersion >= 10;
            }
            catch
            {
                // 回退到 Environment.OSVersion
            }
            return Environment.OSVersion.Version.Major >= 10;
        }

        /// <summary>检测 WebView2 Runtime；未安装时 GetAvailableBrowserVersionString 抛异常</summary>
        private static bool DetectWebView2()
        {
            try
            {
                Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildVersionText()
        {
            var info = new NativeMethods.OSVERSIONINFOEX
            {
                dwOSVersionInfoSize = Marshal.SizeOf(typeof(NativeMethods.OSVERSIONINFOEX)),
            };
            try
            {
                if (NativeMethods.RtlGetVersion(ref info) == 0)
                    return $"{(info.dwMajorVersion >= 10 ? "Windows 10/11" : "Windows")} ({info.dwMajorVersion}.{info.dwMinorVersion}.{info.dwBuildNumber})";
            }
            catch
            {
                // 忽略，使用默认文本
            }
            return Environment.OSVersion.VersionString;
        }
    }
}
