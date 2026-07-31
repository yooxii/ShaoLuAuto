using ShaoLu.Utils;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ShaoLu.Views
{
    /// <summary>
    /// 富文本编辑器窗口
    /// </summary>
    public partial class WindowRichTextEditor : Window
    {
        private string _result;

        public WindowRichTextEditor()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 打开富文本编辑器对话框
        /// </summary>
        /// <param name="content">初始内容（纯文本或 XAML FlowDocument）</param>
        /// <param name="owner">父窗口</param>
        /// <returns>编辑后的内容（纯文本或 XAML），取消返回 null</returns>
        public static string ShowDialog(string content, Window owner = null)
        {
            var editor = new WindowRichTextEditor();
            if (owner != null)
                editor.Owner = owner;

            // 加载内容
            var doc = RichTextHelper.Deserialize(content);
            editor.Editor.Document = doc;

            var result = editor.ShowDialog();
            return result == true ? editor._result : null;
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            _result = RichTextHelper.Serialize(Editor.Document);
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnBold_Click(object sender, RoutedEventArgs e)
        {
            var fontWeight = BtnBold.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            new TextRange(Editor.Selection.Start, Editor.Selection.End).ApplyPropertyValue(TextElement.FontWeightProperty, fontWeight);
            Editor.Focus();
        }

        private void BtnItalic_Click(object sender, RoutedEventArgs e)
        {
            var fontStyle = BtnItalic.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
            new TextRange(Editor.Selection.Start, Editor.Selection.End).ApplyPropertyValue(TextElement.FontStyleProperty, fontStyle);
            Editor.Focus();
        }

        private void BtnUnderline_Click(object sender, RoutedEventArgs e)
        {
            var decoration = BtnUnderline.IsChecked == true ? TextDecorations.Underline : null;
            new TextRange(Editor.Selection.Start, Editor.Selection.End).ApplyPropertyValue(Inline.TextDecorationsProperty, decoration);
            Editor.Focus();
        }

        private void CmbFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Editor == null) return;

            if (CmbFontSize.SelectedItem is ComboBoxItem item && double.TryParse(item.Content.ToString(), out double size))
            {
                new TextRange(Editor.Selection.Start, Editor.Selection.End).ApplyPropertyValue(TextElement.FontSizeProperty, size);
            }
            Editor.Focus();
        }

        private void BtnColor_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.ColorDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = Color.FromArgb(255, dialog.Color.R, dialog.Color.G, dialog.Color.B);
                var brush = new SolidColorBrush(color);
                new TextRange(Editor.Selection.Start, Editor.Selection.End).ApplyPropertyValue(TextElement.ForegroundProperty, brush);
                ColorIndicator.Fill = brush;
            }
            Editor.Focus();
        }

        private void BtnLeft_Click(object sender, RoutedEventArgs e)
        {
            new TextRange(Editor.Selection.Start, Editor.Selection.End).ApplyPropertyValue(Block.TextAlignmentProperty, TextAlignment.Left);
            Editor.Focus();
        }

        private void BtnCenter_Click(object sender, RoutedEventArgs e)
        {
            new TextRange(Editor.Selection.Start, Editor.Selection.End).ApplyPropertyValue(Block.TextAlignmentProperty, TextAlignment.Center);
            Editor.Focus();
        }

        private void BtnRight_Click(object sender, RoutedEventArgs e)
        {
            new TextRange(Editor.Selection.Start, Editor.Selection.End).ApplyPropertyValue(Block.TextAlignmentProperty, TextAlignment.Right);
            Editor.Focus();
        }

        private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            // 同步工具栏状态
            var fontWeight = Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty);
            BtnBold.IsChecked = fontWeight is FontWeight fw && fw == FontWeights.Bold;

            var fontStyle = Editor.Selection.GetPropertyValue(TextElement.FontStyleProperty);
            BtnItalic.IsChecked = fontStyle is FontStyle fs && fs == FontStyles.Italic;

            var decorations = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
            BtnUnderline.IsChecked = decorations is TextDecorationCollection tdc && tdc.Count > 0;
        }
    }
}
