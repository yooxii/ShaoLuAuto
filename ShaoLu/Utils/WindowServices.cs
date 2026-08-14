using System;
using System.Collections.Generic;
using System.Text;

namespace ShaoLu.Utils
{
    /// <summary>
    /// 窗口枚举与置顶操作
    /// </summary>
    public static class WindowServices
    {
        /// <summary>
        /// 枚举当前所有可见且带标题的顶层窗口
        /// </summary>
        public static List<KeyValuePair<IntPtr, string>> GetVisibleTopLevelWindows()
        {
            var list = new List<KeyValuePair<IntPtr, string>>();
            NativeMethods.EnumWindows((hwnd, lParam) =>
            {
                if (NativeMethods.IsWindowVisible(hwnd))
                {
                    int len = NativeMethods.GetWindowTextLength(hwnd);
                    if (len > 0)
                    {
                        var sb = new StringBuilder(len + 1);
                        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
                        list.Add(new KeyValuePair<IntPtr, string>(hwnd, sb.ToString()));
                    }
                }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>
        /// 按标题查找窗口：优先精确匹配 exactTitle，否则按关键字包含匹配（不区分大小写）
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
        /// 查询窗口当前是否处于置顶状态
        /// </summary>
        public static bool IsTopmost(IntPtr hwnd)
        {
            return (NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOPMOST) != 0;
        }

        /// <summary>
        /// 设置或取消窗口置顶
        /// </summary>
        public static bool SetTopmost(IntPtr hwnd, bool topmost)
        {
            return NativeMethods.SetWindowPos(
                hwnd,
                topmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }
}
