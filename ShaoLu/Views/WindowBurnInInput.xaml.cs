using ShaoLu.Models;
using ShaoLu.Services;
using System.Windows;
using System.Windows.Input;

namespace ShaoLu.Views
{
    /// <summary>
    /// 烧录统计输入弹窗：收集工令/操作者/零件名
    /// </summary>
    public partial class WindowBurnInInput : Window
    {
        /// <summary>用户确认后的输入结果</summary>
        public BurnInSession Result { get; private set; }

        public WindowBurnInInput()
        {
            InitializeComponent();
            Loaded += WindowBurnInInput_Loaded;
        }

        private void WindowBurnInInput_Loaded(object sender, RoutedEventArgs e)
        {
            // 记忆上次会话作为默认值
            var last = BurnInService.LoadLastSession();
            if (last != null)
            {
                OrderNoBox.Text = last.OrderNo ?? string.Empty;
                OperatorBox.Text = last.Operator ?? string.Empty;
                PartNameBox.Text = last.PartName ?? string.Empty;
            }
            // 聚焦到第一个为空的输入框
            if (string.IsNullOrEmpty(OrderNoBox.Text)) OrderNoBox.Focus();
            else if (string.IsNullOrEmpty(OperatorBox.Text)) OperatorBox.Focus();
            else if (string.IsNullOrEmpty(PartNameBox.Text)) PartNameBox.Focus();
            else OrderNoBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string orderNo = (OrderNoBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(orderNo))
            {
                MessageBox.Show(
                    LanguageService.GetLocalizedString("BurnIn_OrderNoRequired"),
                    LanguageService.GetLocalizedString("BurnIn_Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                OrderNoBox.Focus();
                return;
            }

            Result = new BurnInSession
            {
                OrderNo = orderNo,
                Operator = (OperatorBox.Text ?? string.Empty).Trim(),
                PartName = (PartNameBox.Text ?? string.Empty).Trim(),
            };
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = null;
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OkButton_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelButton_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
