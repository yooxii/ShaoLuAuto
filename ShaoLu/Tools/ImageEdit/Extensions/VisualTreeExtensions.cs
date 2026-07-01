using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace ImageTool.Extensions
{
    public static class VisualTreeExtensions
    {
        public static T GetVisualChild<T>(this DependencyObject parent) where T : Visual
        {
            T child = default;

            int numVisuals = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < numVisuals; i++)
            {
                Visual v = (Visual)VisualTreeHelper.GetChild(parent, i);
                child = v as T;
                if (child == null)
                {
                    child = GetVisualChild<T>(v);
                }
                if (child != null)
                {
                    break;
                }
            }

            return child;
        }
    }
}
