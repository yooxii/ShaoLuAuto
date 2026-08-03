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
        /// 超过最大长度时截断文本并追加省略号
        /// </summary>
        public static string Truncate(string text, int maxLength = MaxDisplayLength)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "…";
        }

        /// <summary>
        /// 判断文本是否会被截断显示
        /// </summary>
        public static bool IsTruncated(string text, int maxLength = MaxDisplayLength)
        {
            return !string.IsNullOrEmpty(text) && text.Length > maxLength;
        }
    }
}
