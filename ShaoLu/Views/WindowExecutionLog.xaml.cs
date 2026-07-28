using ShaoLu.Viewmodels;
using System.Windows;
using System.Windows.Controls;

namespace ShaoLu.Views
{
    /// <summary>
    /// WindowExecutionLog.xaml 的交互逻辑
    /// </summary>
    public partial class WindowExecutionLog : Window
    {
        private readonly ExecutionLogViewModel _viewModel;

        public WindowExecutionLog()
        {
            InitializeComponent();
            _viewModel = new ExecutionLogViewModel();
            DataContext = _viewModel;
        }

        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            // 阻止 DataGrid 默认的本地排序，改用 ViewModel 的服务端排序
            e.Handled = true;
            string columnName = e.Column.SortMemberPath;
            if (!string.IsNullOrEmpty(columnName))
            {
                _viewModel.ToggleSort(columnName);
            }
        }
    }
}
