using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Viewmodels;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShaoLu.Views
{
    public partial class WindowAsyncPopup : Window
    {
        private TaskCompletionSource<string> _tcs;

        // 缓存系统图标，避免重复进行 GDI+ 到 WPF 的转换，提升性能
        private static readonly Dictionary<MessageBoxImage, ImageSource> IconCache = [];

        static WindowAsyncPopup()
        {
            // 预加载常用图标
            IconCache[MessageBoxImage.Error] = SystemIcons.Error.ToBitmapSource();
            IconCache[MessageBoxImage.Warning] = SystemIcons.Warning.ToBitmapSource();
            IconCache[MessageBoxImage.Question] = SystemIcons.Question.ToBitmapSource();
            IconCache[MessageBoxImage.Information] = SystemIcons.Information.ToBitmapSource();
            IconCache[MessageBoxImage.None] = null;
        }

        public WindowAsyncPopup()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 异步显示弹窗，不阻塞调用线程，允许主窗口响应其他事件（如停止按钮）
        /// </summary>
        public static (Window Window, Task<string> Task) Show(string message, string title, FontModel font, PopupButtons buttons, MessageBoxImage icon)
        {
            var tcs = new TaskCompletionSource<string>();

            if (Application.Current.Dispatcher.CheckAccess())
            {
                return CreateAndShow(message, title, font, buttons, icon, tcs);
            }
            else
            {
                // 必须在 UI 线程创建
                return Application.Current.Dispatcher.Invoke(() => CreateAndShow(message, title, font, buttons, icon, tcs));
            }
        }

        public static (Window Window, Task<string> Task) Show(string message, string title, PopupButtons buttons, MessageBoxImage icon)
        {
            var tcs = new TaskCompletionSource<string>();

            if (Application.Current.Dispatcher.CheckAccess())
            {
                return CreateAndShow(message, title, null, buttons, icon, tcs);
            }
            else
            {
                // 必须在 UI 线程创建
                return Application.Current.Dispatcher.Invoke(() => CreateAndShow(message, title, null, buttons, icon, tcs));
            }
        }

        /// <summary>
        /// 核心创建逻辑，统一处理窗口初始化和显示
        /// </summary>
        private static (Window Window, Task<string> Task) CreateAndShow(string message, string title, FontModel font, PopupButtons buttons, MessageBoxImage icon, TaskCompletionSource<string> tcs)
        {
            var popup = new WindowAsyncPopup
            {
                Title = title ?? string.Empty,
                _tcs = tcs
            };

            // 1. 设置消息文本
            if (popup.MessageText != null)
            {
                popup.MessageText.Text = message ?? string.Empty;

                // 如果提供了字体模型，应用字体设置
                if (font != null)
                {
                    popup.MessageText.FontFamily = new System.Windows.Media.FontFamily(font.FontFamily);
                    popup.MessageText.FontSize = font.FontSize;
                    popup.MessageText.FontStyle = font.FontStyle;
                    popup.MessageText.FontWeight = font.FontWeight;
                    var color = System.Drawing.Color.FromArgb(font.FontColor);
                    popup.MessageText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, color.R, color.G, color.B));
                }
            }

            // 2. 设置图标 (从缓存获取)
            if (popup.IconImage != null)
            {
                if (IconCache.TryGetValue(icon, out var source))
                {
                    popup.IconImage.Source = source;
                }
                else
                {
                    // 兜底：如果没有缓存，尝试动态转换（虽然静态构造已覆盖所有标准情况）
                    popup.IconImage.Source = GetIconSourceDynamic(icon);
                }
            }

            // 3. 生成按钮
            popup.CreateButtons(buttons);

            // 4. 处理窗口关闭事件（防止用户通过 Alt+F4 或右上角 X 关闭时任务挂起）
            popup.Closed += (s, e) =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetResult(string.Empty);
                }
                // 帮助 GC：解除引用
                popup._tcs = null;
            };

            // 5. 非模态显示
            popup.Show();

            // 6. 激活窗口并设置焦点，确保键盘交互正常
            popup.Activate();

            return (popup, tcs.Task);
        }

        private void CreateButtons(PopupButtons buttons)
        {
            if (ButtonPanel == null) return;

            ButtonPanel.Children.Clear();

            void AddButton(string content, string result, bool isDefault = false)
            {
                var btn = new Button
                {
                    Content = content,
                    Width = 80,
                    Height = 30,
                    Margin = new Thickness(5, 0, 0, 0),
                    IsDefault = isDefault // 设置默认按钮，响应 Enter 键
                };

                btn.Click += (s, e) =>
                {
                    // 线程安全地设置结果
                    _tcs?.TrySetResult(result);
                    this.Close();
                };

                ButtonPanel.Children.Add(btn);

                // 如果是默认按钮，设置焦点
                if (isDefault)
                {
                    // 使用 Dispatcher 确保在渲染完成后设置焦点
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        btn.Focus();
                        Keyboard.Focus(btn);
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            foreach (var button in buttons.Buttons)
            {
                if (string.IsNullOrEmpty(button.DisplayText) && button.DefaultValues.Contains(button.Value))
                    button.DisplayText = LanguageService.GetLocalizedString(button.Value);
                AddButton(button.DisplayText, button.Value, button == buttons.DefaultButton);
            }
        }

        private static ImageSource GetIconSourceDynamic(MessageBoxImage icon)
        {
            return icon switch
            {
                MessageBoxImage.Error => SystemIcons.Error.ToBitmapSource(),
                MessageBoxImage.Warning => SystemIcons.Warning.ToBitmapSource(),
                MessageBoxImage.Question => SystemIcons.Question.ToBitmapSource(),
                _ => SystemIcons.Information.ToBitmapSource(),
            };
        }
    }

    // 辅助扩展方法：将 System.Drawing.Icon 转换为 WPF ImageSource
    public static class IconExtensions
    {
        public static ImageSource ToBitmapSource(this System.Drawing.Icon icon)
        {
            if (icon == null) return null;

            try
            {
                return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            catch
            {
                return null;
            }
        }
    }
}