using System;
using System.Windows;
using System.Windows.Input;

namespace ShaoLu.Views
{
    /// <summary>
    /// 通用单行文本输入框：标题、提示、初始值由调用方传入，确定返回输入内容，取消返回 null。
    /// 目前用于图像步骤详情里「重命名」原图。
    /// </summary>
    public partial class WindowTextInput : Window
    {
        /// <summary>输入结果（取消时为 null）</summary>
        public string InputText { get; private set; }

        public WindowTextInput()
        {
            InitializeComponent();
            Loaded += (_, __) => FocusValueBox();
        }

        private void FocusValueBox()
        {
            try
            {
                ValueBox.Focus();
                ValueBox.SelectAll();
                Keyboard.Focus(ValueBox);
            }
            catch
            {
                // 抢焦点失败不影响输入
            }
        }

        private void ValueBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Accept();
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Accept();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            InputText = null;
            DialogResult = false;
        }

        private void Accept()
        {
            string text = (ValueBox.Text ?? string.Empty).Trim();
            if (text.Length == 0)
                return; // 空输入不提交，留在窗口里继续输入

            InputText = text;
            DialogResult = true;
        }

        /// <summary>
        /// 在 UI 线程弹出输入框并等待结果；取消/直接关闭返回 null。
        /// </summary>
        public static string ShowAndWait(string title, string prompt, string initialText = null, Window owner = null)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                return dispatcher.Invoke(() => ShowAndWait(title, prompt, initialText, owner));

            var window = new WindowTextInput
            {
                Title = string.IsNullOrWhiteSpace(title) ? "Rename" : title,
            };
            window.PromptText.Text = prompt ?? string.Empty;
            window.ValueBox.Text = initialText ?? string.Empty;
            window.OkButton.Content = Services.LanguageService.GetLocalizedString("OK", "OK");
            window.CancelButton.Content = Services.LanguageService.GetLocalizedString("Cancel", "Cancel");

            window.Owner = owner ?? Application.Current?.MainWindow;
            return window.ShowDialog() == true ? window.InputText : null;
        }
    }
}
