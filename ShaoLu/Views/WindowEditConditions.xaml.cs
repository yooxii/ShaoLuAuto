using ShaoLu.Services;
using ShaoLu.Viewmodels.AutomationStep;
using System.Windows;

namespace ShaoLu.Views
{
    /// <summary>
    /// 结果判断编辑窗口：把原来内嵌在步骤详情里的条件编辑区独立成窗口，
    /// 步骤详情里只保留“编辑结果判断”入口按钮。
    /// </summary>
    public partial class WindowEditConditions : Window
    {
        public WindowEditConditions(AutomationStepBase step)
        {
            InitializeComponent();
            DataContext = step;

            string title = LanguageService.GetLocalizedString("Condition_EditConditions");
            HeaderText.Text = string.IsNullOrWhiteSpace(step?.Name) ? title : $"{title} - {step.Name}";
            Title = title;
        }

        /// <summary>以模态方式打开指定步骤的条件编辑窗口</summary>
        public static void Edit(AutomationStepBase step)
        {
            if (step == null) return;
            var window = new WindowEditConditions(step)
            {
                Owner = Application.Current?.MainWindow
            };
            window.ShowDialog();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
