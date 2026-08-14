using System;
using System.IO;
using System.Windows;
using System.Windows.Xps.Packaging;

namespace ShaoLu.Views
{
    /// <summary>
    /// XPS 文档预览窗口
    /// </summary>
    public partial class WindowDocumentViewer : Window
    {
        private XpsDocument _xpsDocument;

        public WindowDocumentViewer(string xpsPath)
        {
            InitializeComponent();
            Title = Path.GetFileNameWithoutExtension(xpsPath);
            LoadDocument(xpsPath);
        }

        private void LoadDocument(string xpsPath)
        {
            try
            {
                _xpsDocument = new XpsDocument(xpsPath, FileAccess.Read);
                DocViewer.Document = _xpsDocument.GetFixedDocumentSequence();
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Error(ex, "Load XPS document failed: {0}", xpsPath);
                MessageBox.Show($"{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // 关闭 XpsDocument 释放文件句柄
            _xpsDocument?.Close();
            _xpsDocument = null;
            DocViewer.Document = null;
            base.OnClosed(e);
        }
    }
}
