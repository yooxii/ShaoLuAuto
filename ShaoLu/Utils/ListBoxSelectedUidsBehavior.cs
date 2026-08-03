using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ShaoLu.Utils
{
    /// <summary>
    /// ListBox 多选同步行为：将 ListBox.SelectedItems（步骤对象）与 Guid 列表双向同步
    /// </summary>
    public class ListBoxSelectedUidsBehavior
    {
        public static readonly DependencyProperty SelectedUidsProperty =
            DependencyProperty.RegisterAttached(
                "SelectedUids",
                typeof(IList),
                typeof(ListBoxSelectedUidsBehavior),
                new FrameworkPropertyMetadata(null, OnSelectedUidsChanged));

        public static IList GetSelectedUids(DependencyObject obj) => (IList)obj.GetValue(SelectedUidsProperty);
        public static void SetSelectedUids(DependencyObject obj, IList value) => obj.SetValue(SelectedUidsProperty, value);

        private static void OnSelectedUidsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ListBox listBox)
            {
                listBox.SelectionChanged -= ListBox_SelectionChanged;
                if (e.NewValue != null)
                {
                    SyncSelection(listBox, (IList)e.NewValue);
                    listBox.SelectionChanged += ListBox_SelectionChanged;
                }
            }
        }

        private static void SyncSelection(ListBox listBox, IList uids)
        {
            foreach (var item in listBox.Items)
            {
                if (item is AutomationStepBase step && uids.Contains(step.Uid))
                    listBox.SelectedItems.Add(item);
            }
        }

        private static void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox listBox) return;
            var uids = GetSelectedUids(listBox);
            if (uids == null) return;

            foreach (var item in e.RemovedItems)
            {
                if (item is AutomationStepBase step)
                    uids.Remove(step.Uid);
            }
            foreach (var item in e.AddedItems)
            {
                if (item is AutomationStepBase step && !uids.Contains(step.Uid))
                    uids.Add(step.Uid);
            }
        }
    }
}
