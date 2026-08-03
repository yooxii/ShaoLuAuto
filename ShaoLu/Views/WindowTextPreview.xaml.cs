using System.Windows;

namespace ShaoLu.Views
{
    /// <summary>
    /// 文本预览窗口：显示被截断的完整文本内容
    /// </summary>
    public partial class WindowTextPreview : Window
    {
        public WindowTextPreview()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 打开文本预览窗口（非模态，居中显示）
        /// </summary>
        /// <param name="title">窗口标题</param>
        /// <param name="text">完整文本内容</param>
        public static void Show(string title, string text)
        {
            var window = new WindowTextPreview
            {
                Title = string.IsNullOrEmpty(title)
                    ? Services.LanguageService.GetLocalizedString("TextPreviewTitle")
                    : $"{Services.LanguageService.GetLocalizedString("TextPreviewTitle")} - {title}",
            };
            window.PreviewTextBox.Text = text ?? string.Empty;
            window.Owner = Application.Current.MainWindow;
            window.Show();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(PreviewTextBox.Text))
            {
                Clipboard.SetText(PreviewTextBox.Text);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
