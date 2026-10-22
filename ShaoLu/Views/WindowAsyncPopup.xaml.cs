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

        // 是否为紧凑模式（影响按钮尺寸等）
        private bool _compact;

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
        /// <param name="opacity">背景不透明度（0~1）</param>
        /// <param name="backgroundColor">背景颜色（十六进制，null 使用默认白色）</param>
        /// <param name="windowStyle">窗口样式（正常/紧凑）</param>
        public static (Window Window, Task<string> Task) Show(string message, string title, FontModel font, PopupButtons buttons, MessageBoxImage icon, System.Windows.Point? position = null, double opacity = 1.0, string backgroundColor = null, ShaoLu.Models.PopupWindowStyle windowStyle = ShaoLu.Models.PopupWindowStyle.Normal)
        {
            var tcs = new TaskCompletionSource<string>();

            if (Application.Current.Dispatcher.CheckAccess())
            {
                return CreateAndShow(message, title, font, buttons, icon, tcs, position, opacity, backgroundColor, windowStyle);
            }
            else
            {
                // 必须在 UI 线程创建
                return Application.Current.Dispatcher.Invoke(() => CreateAndShow(message, title, font, buttons, icon, tcs, position, opacity, backgroundColor, windowStyle));
            }
        }

        public static (Window Window, Task<string> Task) Show(string message, string title, PopupButtons buttons, MessageBoxImage icon)
        {
            var tcs = new TaskCompletionSource<string>();

            if (Application.Current.Dispatcher.CheckAccess())
            {
                return CreateAndShow(message, title, null, buttons, icon, tcs, null, 1.0, null, ShaoLu.Models.PopupWindowStyle.Normal);
            }
            else
            {
                // 必须在 UI 线程创建
                return Application.Current.Dispatcher.Invoke(() => CreateAndShow(message, title, null, buttons, icon, tcs, null, 1.0, null, ShaoLu.Models.PopupWindowStyle.Normal));
            }
        }

        /// <summary>
        /// 核心创建逻辑，统一处理窗口初始化和显示
        /// </summary>
        private static (Window Window, Task<string> Task) CreateAndShow(string message, string title, FontModel font, PopupButtons buttons, MessageBoxImage icon, TaskCompletionSource<string> tcs, System.Windows.Point? position = null, double opacity = 1.0, string backgroundColor = null, ShaoLu.Models.PopupWindowStyle windowStyle = ShaoLu.Models.PopupWindowStyle.Normal)
        {
            var popup = new WindowAsyncPopup
            {
                Title = title ?? string.Empty,
                _tcs = tcs
            };

            // 应用自定义位置
            if (position.HasValue)
            {
                popup.WindowStartupLocation = WindowStartupLocation.Manual;
                popup.Left = position.Value.X;
                popup.Top = position.Value.Y;
            }

            // 应用背景颜色与不透明度：未指定时跟随主题；指定时整窗不透明度（原生窗口无法只透明背景）
            if (!string.IsNullOrWhiteSpace(backgroundColor))
            {
                try
                {
                    var bgColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(backgroundColor);
                    popup.Background = new System.Windows.Media.SolidColorBrush(bgColor);
                }
                catch { /* 解析失败保持主题背景 */ }
            }
            popup.Opacity = Math.Min(1.0, Math.Max(0.05, opacity));

            // 1. 设置消息内容（支持富文本）
            bool emptyDoc = true;
            if (popup.MessageViewer != null)
            {
                var doc = Utils.RichTextHelper.Deserialize(message ?? string.Empty);
                emptyDoc = doc.Blocks.Count == 0;

                // 文档按视口宽度排版：ColumnWidth/PageWidth = NaN，绝不写超大的列宽（否则紧凑模式会被裁字）
                doc.PagePadding = new Thickness(0);
                doc.ColumnWidth = double.NaN;
                doc.PageWidth = double.NaN;

                // 字体：未指定时回落到中英文都协调的字体链，避免 Arial 把中文交给度量不一致的回退字体
                string family = string.IsNullOrWhiteSpace(font?.FontFamily) ? "Microsoft YaHei UI, Segoe UI" : font.FontFamily;
                doc.FontFamily = new System.Windows.Media.FontFamily(family);

                double size = font == null || font.FontSize <= 0 ? 14 : font.FontSize;
                if (popup._compact) size = Math.Min(14, Math.Max(11, size));
                doc.FontSize = size;

                if (font != null)
                {
                    doc.FontStyle = font.FontStyle;
                    doc.FontWeight = font.FontWeight;
                }

                // 前景色：默认黑字；背景是深色且字体色仍是默认黑时自动反白，避免黑字黑底
                var fg = System.Windows.Media.Colors.Black;
                if (font != null)
                {
                    var color = System.Drawing.Color.FromArgb(font.FontColor);
                    fg = System.Windows.Media.Color.FromArgb(255, color.R, color.G, color.B);
                }
                if (IsDarkBrush(popup.Background) && fg == System.Windows.Media.Colors.Black)
                    fg = System.Windows.Media.Colors.White;
                doc.Foreground = new SolidColorBrush(fg);

                popup.MessageViewer.Document = doc;
            }

            // 尺寸规范：正常 320~600 × 140~540；紧凑 200~420 × 104~250（不打断、不占地）
            if (popup._compact)
            {
                popup.MinWidth = 100;
                popup.MinHeight = 104;
                popup.MaxWidth = 300;
                popup.MaxHeight = 400;
                if (popup.LayoutRoot != null)
                    popup.LayoutRoot.Margin = new Thickness(10, 8, 10, 8);
                if (popup.IconImage != null)
                {
                    popup.IconImage.Width = 16;
                    popup.IconImage.Height = 16;
                    popup.IconImage.Margin = new Thickness(0, 0, 8, 0);
                }
                if (popup.ButtonHost != null)
                    popup.ButtonHost.Margin = new Thickness(0, 10, 0, 0);
                if (popup.ButtonPanel != null)
                    popup.ButtonPanel.ItemHeight = 24;
            }
            else
            {
                popup.MinWidth = 120;
                popup.MinHeight = 140;
                popup.MaxWidth = 400;
                popup.MaxHeight = 400;
                if (popup.ButtonHost != null)
                    popup.ButtonHost.Margin = new Thickness(0, 16, 0, 0);
                if (popup.ButtonPanel != null)
                    popup.ButtonPanel.ItemHeight = 32;
            }
            
            // 2. 设置图标（无图标时不占空间）
            if (popup.IconImage != null)
            {
                if (icon == MessageBoxImage.None)
                {
                    popup.IconImage.Visibility = Visibility.Collapsed;
                }
                else if (IconCache.TryGetValue(icon, out var source))
                {
                    popup.IconImage.Source = source;
                }
                else
                {
                    // 兜底：如果没有缓存，尝试动态转换（虽然静态构造已覆盖所有标准情况）
                    popup.IconImage.Source = GetIconSourceDynamic(icon);
                }
            }
            
            // 3. 生成按钮（未设置按钮时不占空间）
            popup.CreateButtons(buttons);
            if ((buttons?.Buttons?.Count ?? 0) == 0 && popup.ButtonPanel != null)
            {
                popup.ButtonPanel.Visibility = Visibility.Collapsed;
            }

            // 3.1 无正文时：宽度只保留「能放下图标 + 50」再加两侧边距；
            // 有正文时宽度按内容长度自适应（最多 400），超过的内容自动换到下一行。
            if (emptyDoc && popup.IconImage != null)
            {
                double iconWidth = popup.IconImage.Visibility == Visibility.Collapsed ? 0 : popup.IconImage.Width;
                double sideMargins = popup._compact ? 20 : 40;
                popup.Width = Math.Max(popup.MinWidth, iconWidth + 50 + sideMargins);
                popup.SizeToContent = SizeToContent.Height;
            }

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

            // 5. 两段式尺寸：先按内容自适应，Loaded 后锁住，避免用户缩放被内容重算打回
            popup.Loaded += (_, __) =>
            {
                popup.SizeToContent = SizeToContent.Manual;
                if (popup.ActualWidth > 0) popup.Width = popup.ActualWidth;
                if (popup.ActualHeight > 0) popup.Height = popup.ActualHeight;
            };

            // 6. 非模态显示
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
                    MinWidth = _compact ? 56 : 88,
                    Height = _compact ? 24 : 32,
                    Padding = new Thickness(_compact ? 10 : 14, 0, _compact ? 10 : 14, 0),
                    Margin = new Thickness(_compact ? 6 : 8, 0, 0, 0),
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

        /// <summary>背景是否为深色（相对亮度阈值），用于默认黑字在深底上自动反白</summary>
        private static bool IsDarkBrush(System.Windows.Media.Brush brush)
        {
            if (brush is not System.Windows.Media.SolidColorBrush solid) return false;
            var c = solid.Color;
            double luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
            return luminance < 0.45;
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

        /// <summary>
        /// 从外部触发关闭弹窗并设置结果（供超时/步骤到达模式使用）
        /// </summary>
        public void CloseWithResult(string result)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => CloseWithResult(result));
                return;
            }

            _tcs?.TrySetResult(result ?? string.Empty);
            if (IsVisible)
            {
                Close();
            }
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