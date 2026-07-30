using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Xml;

namespace ShaoLu.Utils
{
    /// <summary>
    /// 富文本序列化/反序列化辅助类
    /// 存储格式为 XAML FlowDocument 字符串，兼容纯文本
    /// </summary>
    public static class RichTextHelper
    {
        /// <summary>
        /// 判断字符串是否为富文本格式（XAML FlowDocument）
        /// </summary>
        public static bool IsRichText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.TrimStart().StartsWith("<FlowDocument") || text.TrimStart().StartsWith("<Section");
        }

        /// <summary>
        /// 将 FlowDocument 序列化为 XAML 字符串
        /// </summary>
        public static string Serialize(FlowDocument doc)
        {
            if (doc == null) return string.Empty;

            // 检查是否为纯文本（只有一个段落且无格式）
            string plainText = GetPlainText(doc);
            if (IsPlainTextOnly(doc))
                return plainText;

            var sb = new StringBuilder();
            using (var writer = new StringWriter(sb))
            {
                XamlWriter.Save(doc, writer);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 将字符串反序列化为 FlowDocument（兼容纯文本）
        /// </summary>
        public static FlowDocument Deserialize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new FlowDocument(new Paragraph(new Run("")));

            if (IsRichText(text))
            {
                try
                {
                    using (var reader = new StringReader(text))
                    using (var xmlReader = XmlReader.Create(reader))
                    {
                        var doc = (FlowDocument)XamlReader.Load(xmlReader);
                        return doc;
                    }
                }
                catch
                {
                    // 解析失败，作为纯文本处理
                }
            }

            // 纯文本：创建包含文本的 FlowDocument
            var plainDoc = new FlowDocument();
            var paragraph = new Paragraph();
            // 支持换行
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                paragraph.Inlines.Add(new Run(lines[i]));
                if (i < lines.Length - 1)
                    paragraph.Inlines.Add(new LineBreak());
            }
            plainDoc.Blocks.Add(paragraph);
            return plainDoc;
        }

        /// <summary>
        /// 从 FlowDocument 提取纯文本
        /// </summary>
        public static string GetPlainText(FlowDocument doc)
        {
            if (doc == null) return string.Empty;
            return new TextRange(doc.ContentStart, doc.ContentEnd).Text.TrimEnd('\r', '\n');
        }

        /// <summary>
        /// 判断 FlowDocument 是否仅包含纯文本（无格式）
        /// </summary>
        private static bool IsPlainTextOnly(FlowDocument doc)
        {
            if (doc.Blocks.Count != 1) return false;
            if (!(doc.Blocks.FirstBlock is Paragraph para)) return false;

            foreach (var inline in para.Inlines)
            {
                if (inline is Run run)
                {
                    // 检查是否有非默认格式
                    if (run.FontWeight != FontWeights.Normal) return false;
                    if (run.FontStyle != FontStyles.Normal) return false;
                    if (run.Foreground != System.Windows.Media.Brushes.Black &&
                        run.Foreground != null &&
                        run.Foreground.ToString() != "#FF000000") return false;
                    if (run.FontSize != 14.0 && run.FontSize != 12.0) return false;
                }
                else if (inline is LineBreak)
                {
                    // 换行可以接受
                }
                else
                {
                    return false;
                }
            }
            return true;
        }
    }
}
