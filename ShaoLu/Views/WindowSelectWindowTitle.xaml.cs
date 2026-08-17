using ShaoLu.Utils;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace ShaoLu.Views
{
    /// <summary>
    /// 窗口选择对话框：枚举当前可见窗口供用户选择（用于窗口截图/窗口聚焦等）
    /// </summary>
    public partial class WindowSelectWindowTitle : Window
    {
        /// <summary>用户选中的窗口句柄；取消为 IntPtr.Zero</summary>
        public IntPtr SelectedHandle { get; private set; } = IntPtr.Zero;

        private readonly List<KeyValuePair<IntPtr, string>> _windows = new();

        public WindowSelectWindowTitle()
        {
            InitializeComponent();
            RefreshWindows();
        }

        private void RefreshWindows()
        {
            _windows.Clear();
            WindowList.Items.Clear();
            try
            {
                foreach (var w in WindowServices.GetVisibleTopLevelWindows())
                {
                    _windows.Add(w);
                    WindowList.Items.Add(w.Value);
                }
            }
            catch
            {
                // 枚举失败时保持空列表
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshWindows();
            OkButton.IsEnabled = false;
        }

        private void WindowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            OkButton.IsEnabled = WindowList.SelectedIndex >= 0;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            int index = WindowList.SelectedIndex;
            if (index < 0 || index >= _windows.Count) return;
            SelectedHandle = _windows[index].Key;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
