using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace ShaoLu.Utils
{
    public class DataGridSelectedItemsBehavior
    {
        public static readonly DependencyProperty SelectedItemsProperty =
            DependencyProperty.RegisterAttached(
                "SelectedItems",
                typeof(IList),
                typeof(DataGridSelectedItemsBehavior),
                new FrameworkPropertyMetadata(null, OnSelectedItemsChanged));

        public static IList GetSelectedItems(DependencyObject obj)
        {
            return (IList)obj.GetValue(SelectedItemsProperty);
        }

        public static void SetSelectedItems(DependencyObject obj, IList value)
        {
            obj.SetValue(SelectedItemsProperty, value);
        }

        private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid dataGrid)
            {
                // 移除旧的事件监听
                dataGrid.SelectionChanged -= DataGrid_SelectionChanged;

                // 初始同步
                if (e.NewValue != null)
                {
                    SyncSelectedItems(dataGrid, (IList)e.NewValue);
                    dataGrid.SelectionChanged += DataGrid_SelectionChanged;
                }
            }
        }

        private static void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid dataGrid)
            {
                var selectedItems = GetSelectedItems(dataGrid);
                if (selectedItems != null)
                {
                    SyncSelectedItems(dataGrid, selectedItems);
                }
            }
        }

        private static void SyncSelectedItems(DataGrid dataGrid, IList targetList)
        {
            targetList.Clear();
            foreach (var item in dataGrid.SelectedItems)
            {
                targetList.Add(item);
            }
        }
    }
}