using System;
using System.Windows.Automation;

namespace ShaoLu.Utils
{
    /// <summary>
    /// 屏幕可选文本读取器：通过 UI Automation 读取屏幕上指定坐标处控件的文本内容
    /// </summary>
    public static class ScreenTextReader
    {
        /// <summary>
        /// 读取物理屏幕坐标处元素的可选择文本。
        /// 依次尝试 TextPattern、ValuePattern，最后向上遍历父元素取 Name/Value。
        /// </summary>
        /// <param name="physicalX">物理像素 X 坐标</param>
        /// <param name="physicalY">物理像素 Y 坐标</param>
        /// <returns>读取到的文本，未找到时返回空字符串</returns>
        public static string ReadTextAtPoint(double physicalX, double physicalY)
        {
            try
            {
                AutomationElement element = AutomationElement.FromPoint(new System.Windows.Point(physicalX, physicalY));
                if (element == null) return string.Empty;

                // 1. TextPattern（支持文本选择的控件，如 TextBox、文档）
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out object textPatternObj)
                    && textPatternObj is TextPattern textPattern)
                {
                    string text = textPattern.DocumentRange.GetText(-1);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }

                // 2. ValuePattern（大多数输入/显示控件）
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePatternObj)
                    && valuePatternObj is ValuePattern valuePattern)
                {
                    string value = valuePattern.Current.Value;
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }

                // 3. 向上遍历父元素，尝试获取 Name 或文本模式
                AutomationElement current = element;
                for (int i = 0; i < 5 && current != null; i++)
                {
                    string name = current.Current.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                        return name.Trim();

                    // 父元素的 ValuePattern 也尝试一次
                    try
                    {
                        current = TreeWalker.RawViewWalker.GetParent(current);
                        if (current != null
                            && current.TryGetCurrentPattern(ValuePattern.Pattern, out object parentValueObj)
                            && parentValueObj is ValuePattern parentValue
                            && !string.IsNullOrWhiteSpace(parentValue.Current.Value))
                        {
                            return parentValue.Current.Value.Trim();
                        }
                    }
                    catch
                    {
                        break;
                    }
                }

                return string.Empty;
            }
            catch (Exception)
            {
                // 坐标处无元素或 UIA 不可用时返回空
                return string.Empty;
            }
        }
    }
}
