using System;
using System.IO;
using System.Windows;

namespace ShaoLu.Views
{
    /// <summary>
    /// PDF 文档预览窗口（基于 WebView2，适用于 Win10+）
    /// </summary>
    public partial class WindowPdfViewer : Window
    {
        public WindowPdfViewer(string pdfPath)
        {
            InitializeComponent();
            Title = Path.GetFileNameWithoutExtension(pdfPath);

            // 窗口显示后再初始化 WebView2，避免初始化过程中的布局问题
            Loaded += async (s, e) =>
            {
                try
                {
                    await PdfWebView.EnsureCoreWebView2Async();
                    PdfWebView.Source = new Uri(pdfPath);
                }
                catch (Exception ex)
                {
                    NLog.LogManager.GetCurrentClassLogger().Error(ex, "Load PDF document failed: {0}", pdfPath);
                    MessageBox.Show($"{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                }
            };
        }
    }
}
