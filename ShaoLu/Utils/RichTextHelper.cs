using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml;

namespace ShaoLu.Utils
{
    /// <summary>
    /// 富文本序列化/反序列化辅助类
    /// 存储格式为 XAML FlowDocument 字符串，兼容纯文本。
    /// 注意：反序列化不做通用 XAML 解析（XamlReader.Load 可实例化任意类型，存在不受信任 XAML 风险），
    /// 只按白名单重建 FlowDocument/Paragraph/Run/Bold/Italic/Underline/LineBreak 等受限元素；
    /// 命中白名单外的内容则整体回退为纯文本。
    /// </summary>
    public static class RichTextHelper
    {
        /// <summary>XAML 语言命名空间（x:Name、xml:space 等属性一律忽略）</summary>
        private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        /// <summary>允许从 XAML 还原的富文本元素白名单（仅排版元素，无任何可实例化任意类型的入口）</summary>
        private static readonly HashSet<string> AllowedElements = new HashSet<string>(StringComparer.Ordinal)
        {
            "FlowDocument",
            "Section",
            "Paragraph",
            "Span",
            "Bold",
            "Italic",
            "Underline",
            "Run",
            "LineBreak",
        };

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
        /// 将字符串反序列化为 FlowDocument（兼容纯文本）。
        /// 富文本走受限白名单解析；解析失败或含非白名单元素时按键纯文本处理。
        /// </summary>
        public static FlowDocument Deserialize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new FlowDocument(new Paragraph(new Run("")));

            if (IsRichText(text) && TryParseRestricted(text, out var doc))
                return doc;

            // 纯文本：创建包含文本的 FlowDocument（同时也是富文本解析失败时的安全回退）
            return CreatePlainDocument(text);
        }

        /// <summary>
        /// 受限解析：只按白名单重建排版元素，不使用 XamlReader，杜绝不受信任 XAML 实例化任意类型。
        /// </summary>
        private static bool TryParseRestricted(string xaml, out FlowDocument doc)
        {
            doc = null;
            try
            {
                // 安全读取：禁用 DTD 与外部实体解析
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                    CheckCharacters = false,
                };

                var xmlDoc = new XmlDocument { XmlResolver = null };
                using (var reader = XmlReader.Create(new StringReader(xaml), settings))
                {
                    xmlDoc.Load(reader);
                }

                var root = xmlDoc.DocumentElement;
                if (root == null) return false;
                if (!AllowedElements.Contains(root.LocalName)) return false;

                var result = new FlowDocument();
                if (!ParseContainer(root, result.Blocks, null, isRoot: true))
                    return false;

                doc = result;
                return true;
            }
            catch
            {
                // 任何解析异常都回退纯文本，不把异常抛给界面
                return false;
            }
        }

        /// <summary>
        /// 递归重建子节点；blocks 与 inlines 恰好一个非空（块容器 / 内联容器）。
        /// </summary>
        private static bool ParseContainer(XmlNode parent, BlockCollection blocks, InlineCollection inlines, bool isRoot = false)
        {
            foreach (XmlNode node in parent.ChildNodes)
            {
                switch (node.NodeType)
                {
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                        AppendText(node.Value, blocks, inlines);
                        break;

                    case XmlNodeType.Element:
                        if (isRoot && node is XmlElement rootElement && rootElement.LocalName == "FlowDocument")
                            return false; // FlowDocument 只允许作为根元素
                        if (!ParseElement((XmlElement)node, blocks, inlines))
                            return false;
                        break;

                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                    case XmlNodeType.Comment:
                        // XmlDocument 默认已剥离无意义空白；其余直接忽略
                        break;

                    default:
                        // 其它节点类型（PI 等）忽略
                        break;
                }
            }
            return true;
        }

        /// <summary>把一段文本放入当前容器：内联容器直接加 Run，块容器包一层 Paragraph</summary>
        private static void AppendText(string text, BlockCollection blocks, InlineCollection inlines)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (inlines != null)
            {
                inlines.Add(new Run(text));
            }
            else if (blocks != null)
            {
                blocks.Add(new Paragraph(new Run(text)));
            }
        }

        /// <summary>按白名单重建单个元素；不在白名单内返回 false（由上层回退纯文本）</summary>
        private static bool ParseElement(XmlElement element, BlockCollection blocks, InlineCollection inlines)
        {
            string name = element.LocalName;
            if (!AllowedElements.Contains(name)) return false;

            switch (name)
            {
                case "Section":
                {
                    if (blocks == null) return false;
                    var section = new Section();
                    if (!ParseContainer(element, section.Blocks, null)) return false;
                    blocks.Add(section);
                    return true;
                }

                case "Paragraph":
                {
                    if (blocks == null) return false;
                    var paragraph = new Paragraph();
                    ApplyTextProperties(paragraph, element);
                    ApplyParagraphProperties(paragraph, element);
                    if (!ParseContainer(element, null, paragraph.Inlines)) return false;
                    blocks.Add(paragraph);
                    return true;
                }

                case "Span":
                case "Bold":
                case "Italic":
                case "Underline":
                {
                    if (inlines == null) return false;
                    Span span = name switch
                    {
                        "Bold" => new Bold(),
                        "Italic" => new Italic(),
                        "Underline" => new Underline(),
                        _ => new Span(),
                    };
                    ApplyTextProperties(span, element);
                    if (!ParseContainer(element, null, span.Inlines)) return false;
                    inlines.Add(span);
                    return true;
                }

                case "Run":
                {
                    if (inlines == null) return false;
                    if (!TryGetRunText(element, out string runText)) return false;
                    var run = new Run(runText);
                    ApplyTextProperties(run, element);
                    inlines.Add(run);
                    return true;
                }

                case "LineBreak":
                {
                    if (inlines == null) return false;
                    inlines.Add(new LineBreak());
                    return true;
                }

                default:
                    // FlowDocument 作为子元素出现：不合法
                    return false;
            }
        }

        /// <summary>Run 的文本内容只能是文本/CDATA；含子元素则判定不支持</summary>
        private static bool TryGetRunText(XmlElement element, out string text)
        {
            var sb = new StringBuilder();
            foreach (XmlNode child in element.ChildNodes)
            {
                switch (child.NodeType)
                {
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        sb.Append(child.Value);
                        break;
                    case XmlNodeType.Comment:
                        break;
                    default:
                        text = null;
                        return false;
                }
            }
            text = sb.ToString();
            return true;
        }

        /// <summary>
        /// 应用文本类属性（字号/字重/斜体/字体/前景色/背景色/下划线），未知属性直接忽略。
        /// </summary>
        private static void ApplyTextProperties(TextElement element, XmlElement source)
        {
            if (element == null || source == null) return;

            foreach (XmlAttribute attr in source.Attributes)
            {
                if (attr.NamespaceURI == XamlNamespace) continue;

                switch (attr.LocalName)
                {
                    case "FontSize":
                        if (double.TryParse(attr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double fontSize)
                            && fontSize > 0 && fontSize < 1000)
                        {
                            element.FontSize = fontSize;
                        }
                        break;

                    case "FontWeight":
                        if (TryParseFontWeight(attr.Value, out FontWeight weight))
                            element.FontWeight = weight;
                        break;

                    case "FontStyle":
                        if (TryParseFontStyle(attr.Value, out FontStyle style))
                            element.FontStyle = style;
                        break;

                    case "FontFamily":
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(attr.Value))
                                element.FontFamily = new FontFamily(attr.Value);
                        }
                        catch { /* 非法字体名忽略 */ }
                        break;

                    case "Foreground":
                        if (TryParseBrush(attr.Value, out Brush foreground))
                            element.Foreground = foreground;
                        break;

                    case "Background":
                        if (TryParseBrush(attr.Value, out Brush background))
                            element.Background = background;
                        break;

                    case "TextDecorations":
                        if (element is Inline inline && attr.Value != null
                            && attr.Value.IndexOf("Underline", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            inline.TextDecorations = TextDecorations.Underline;
                        }
                        break;

                    default:
                        // 白名单之外的属性一律忽略（不参与实例化）
                        break;
                }
            }
        }

        /// <summary>应用段落属性（目前只支持对齐方式）</summary>
        private static void ApplyParagraphProperties(Paragraph paragraph, XmlElement source)
        {
            var attr = source.GetAttributeNode("TextAlignment");
            if (attr == null) return;

            switch ((attr.Value ?? string.Empty).Trim())
            {
                case "Left": paragraph.TextAlignment = TextAlignment.Left; break;
                case "Center": paragraph.TextAlignment = TextAlignment.Center; break;
                case "Right": paragraph.TextAlignment = TextAlignment.Right; break;
                case "Justify": paragraph.TextAlignment = TextAlignment.Justify; break;
                default: break;
            }
        }

        private static bool TryParseFontWeight(string value, out FontWeight weight)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "thin": weight = FontWeights.Thin; return true;
                case "extralight": weight = FontWeights.ExtraLight; return true;
                case "light": weight = FontWeights.Light; return true;
                case "normal": weight = FontWeights.Normal; return true;
                case "medium": weight = FontWeights.Medium; return true;
                case "semibold": weight = FontWeights.SemiBold; return true;
                case "bold": weight = FontWeights.Bold; return true;
                case "extrabold": weight = FontWeights.ExtraBold; return true;
                case "black":
                case "heavy": weight = FontWeights.Black; return true;
                default: weight = FontWeights.Normal; return false;
            }
        }

        private static bool TryParseFontStyle(string value, out FontStyle style)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "normal": style = FontStyles.Normal; return true;
                case "italic": style = FontStyles.Italic; return true;
                case "oblique": style = FontStyles.Oblique; return true;
                default: style = FontStyles.Normal; return false;
            }
        }

        /// <summary>把颜色字符串（如 #FF000000 / Red）转成画刷；非法值返回 false</summary>
        private static bool TryParseBrush(string value, out Brush brush)
        {
            brush = null;
            try
            {
                if (string.IsNullOrWhiteSpace(value)) return false;
                if (ColorConverter.ConvertFromString(value.Trim()) is Color color)
                {
                    brush = new SolidColorBrush(color);
                    return true;
                }
            }
            catch { /* 非法颜色忽略 */ }
            return false;
        }

        /// <summary>纯文本 → FlowDocument（支持换行）</summary>
        private static FlowDocument CreatePlainDocument(string text)
        {
            var plainDoc = new FlowDocument();
            var paragraph = new Paragraph();
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
