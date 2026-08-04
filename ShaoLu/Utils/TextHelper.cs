namespace ShaoLu.Utils
{
    /// <summary>
    /// 文本显示辅助工具
    /// </summary>
    public static class TextHelper
    {
        /// <summary>显示文本的最大长度，超过则截断</summary>
        public const int MaxDisplayLength = 100;

        /// <summary>
        /// 压缩文本为单行：将换行/制表符替换为空格，并合并连续空格，
        /// 使表格概览尽量简短地展示更多字符
        /// </summary>
        public static string CompressToSingleLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return System.Text.RegularExpressions.Regex.Replace(text, @"[\r\n\t]+", " ").Replace("  ", " ").Trim();
        }

        /// <summary>
        /// 压缩为单行后，超过最大长度时截断并追加省略号
        /// </summary>
        public static string Truncate(string text, int maxLength = MaxDisplayLength)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string compressed = CompressToSingleLine(text);
            if (compressed.Length <= maxLength) return compressed;
            return compressed.Substring(0, maxLength) + "…";
        }

        /// <summary>
        /// 判断文本是否会被截断或压缩显示（预览全文时用于决定是否提供预览入口）
        /// </summary>
        public static bool IsTruncated(string text, int maxLength = MaxDisplayLength)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string compressed = CompressToSingleLine(text);
            return compressed != text || compressed.Length > maxLength;
        }
    }
}
