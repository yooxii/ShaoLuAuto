using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ShaoLu.Utils
{
    /// <summary>
    /// 窗口枚举、置顶与最小化操作
    /// </summary>
    public static class WindowServices
    {
        /// <summary>本程序进程号（枚举窗口时用来把自身窗口排在最前面）</summary>
        private static readonly uint SelfProcessId = (uint)Process.GetCurrentProcess().Id;

        /// <summary>
        /// 枚举当前所有可见且带标题的顶层窗口；本程序自己的窗口排在最前面
        /// </summary>
        public static List<KeyValuePair<IntPtr, string>> GetVisibleTopLevelWindows()
        {
            var self = new List<KeyValuePair<IntPtr, string>>();
            var others = new List<KeyValuePair<IntPtr, string>>();

            NativeMethods.EnumWindows((hwnd, lParam) =>
            {
                if (NativeMethods.IsWindowVisible(hwnd))
                {
                    int len = NativeMethods.GetWindowTextLength(hwnd);
                    if (len > 0)
                    {
                        var sb = new StringBuilder(len + 1);
                        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
                        var item = new KeyValuePair<IntPtr, string>(hwnd, sb.ToString());
                        if (IsSelfWindow(hwnd)) self.Add(item);
                        else others.Add(item);
                    }
                }
                return true;
            }, IntPtr.Zero);

            self.AddRange(others);
            return self;
        }

        /// <summary>
        /// 按标题查找窗口：优先精确匹配 exactTitle，否则按关键字包含匹配（不区分大小写）
        /// 窗口列表里本程序自己的窗口在最前面，所以同名时优先命中自身窗口
        /// </summary>
        public static IntPtr? FindWindow(string exactTitle, string keyword)
        {
            var windows = GetVisibleTopLevelWindows();

            if (!string.IsNullOrEmpty(exactTitle))
            {
                foreach (var w in windows)
                {
                    if (w.Value == exactTitle) return w.Key;
                }
            }

            if (!string.IsNullOrEmpty(keyword))
            {
                foreach (var w in windows)
                {
                    if (w.Value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return w.Key;
                }
            }

            return null;
        }

        /// <summary>
        /// 置前并激活窗口：最小化时先恢复，然后置于最前并激活
        /// </summary>
        public static bool FocusWindow(IntPtr hwnd)
        {
            if (NativeMethods.IsIconic(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            return NativeMethods.SetForegroundWindow(hwnd);
        }

        /// <summary>
        /// 最小化窗口：调用后按窗口是否真的变成最小化判断成败
        /// </summary>
        public static bool MinimizeWindow(IntPtr hwnd)
        {
            if (NativeMethods.IsIconic(hwnd)) return true;
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
            return NativeMethods.IsIconic(hwnd);
        }

        /// <summary>窗口是否属于本程序进程</summary>
        private static bool IsSelfWindow(IntPtr hwnd)
        {
            try
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                return pid == SelfProcessId;
            }
            catch
            {
                return false;
            }
        }
    }
}
