using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShaoLu.Views
{
    public partial class WindowAsyncPopup : Window
    {
        private TaskCompletionSource<MessageBoxResult> _tcs;

        public WindowAsyncPopup()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 异步显示弹窗，不阻塞调用线程，允许主窗口响应其他事件（如停止按钮）
        /// </summary>
        public static (Window Window, Task<MessageBoxResult> Task) Show(string message, string title, MessageBoxButton button, MessageBoxImage icon)
        {
            var tcs = new TaskCompletionSource<MessageBoxResult>();

            if (Application.Current.Dispatcher.CheckAccess())
            {
                return CreateAndShow(message, title, button, icon, tcs);
            }
            else
            {
                // 必须在 UI 线程创建
                return Application.Current.Dispatcher.Invoke(() => CreateAndShow(message, title, button, icon, tcs));
            }
        }

        private static (Window Window, Task<MessageBoxResult> Task) CreateAndShow(string message, string title, MessageBoxButton button, MessageBoxImage icon, TaskCompletionSource<MessageBoxResult> tcs)
        {
            var popup = new WindowAsyncPopup
            {
                Title = title ?? "",
                _tcs = tcs
            };

            // 设置消息
            popup.MessageText.Text = message ?? "";

            // 设置图标
            popup.IconImage.Source = GetIconSource(icon) ?? null;

            // 生成按钮
            popup.CreateButtons(button);

            // 处理窗口关闭事件（防止用户通过 Alt+F4 或右上角 X 关闭时任务挂起）
            popup.Closed += (s, e) =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetResult(MessageBoxResult.None);
                }
            };

            // 非模态显示
            popup.Show();

            return (popup, tcs.Task);
        }

        private void CreateButtons(MessageBoxButton buttonType)
        {
            ButtonPanel.Children.Clear();

            void AddButton(string content, MessageBoxResult result)
            {
                var btn = new Button
                {
                    Content = content,
                    Width = 80,
                    Height = 30,
                    Margin = new Thickness(5, 0, 0, 0)
                };
                btn.Click += (s, e) =>
                {
                    _tcs?.TrySetResult(result);
                    this.Close();
                };
                ButtonPanel.Children.Add(btn);

                // 设置默认焦点到第一个按钮
                if (ButtonPanel.Children.Count == 1)
                {
                    btn.Focus();
                }
            }

            switch (buttonType)
            {
            case MessageBoxButton.OK:
                AddButton("OK", MessageBoxResult.OK);
                break;
            case MessageBoxButton.OKCancel:
                AddButton("OK", MessageBoxResult.OK);
                AddButton("Cancel", MessageBoxResult.Cancel);
                break;
            case MessageBoxButton.YesNo:
                AddButton("Yes", MessageBoxResult.Yes);
                AddButton("No", MessageBoxResult.No);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("Yes", MessageBoxResult.Yes);
                AddButton("No", MessageBoxResult.No);
                AddButton("Cancel", MessageBoxResult.Cancel);
                break;
            }
        }

        private static ImageSource GetIconSource(MessageBoxImage icon)
        {
            // 使用 WPF 系统图标，无需外部图片资源
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
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
    }
}