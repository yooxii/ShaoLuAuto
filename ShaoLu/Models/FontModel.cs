using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace ShaoLu.Models
{
    /// <summary>
    /// WPF 的 FontWeight 没有可序列化的公共成员，System.Text.Json 会写成 {}，读回时变成默认值（Normal）。
    /// 本转换器改为存 OpenType 数值（如 400/700），旧文件里的 {} / null / 无法识别的值一律回退为 Normal，保证不崩。
    /// </summary>
    public sealed class FontWeightJsonConverter : JsonConverter<FontWeight>
    {
        public override FontWeight Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    return reader.TryGetInt32(out int weight) ? ToFontWeight(weight) : FontWeights.Normal;

                case JsonTokenType.String:
                    string text = reader.GetString();
                    if (int.TryParse(text, out int parsed))
                        return ToFontWeight(parsed);
                    if (string.Equals(text, "Bold", StringComparison.OrdinalIgnoreCase))
                        return FontWeights.Bold;
                    return FontWeights.Normal;

                default:
                    // 旧版本写入的 {}、null 或其他异常数据：跳过并回退默认字重
                    reader.Skip();
                    return FontWeights.Normal;
            }
        }

        public override void Write(Utf8JsonWriter writer, FontWeight value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value.ToOpenTypeWeight());
        }

        private static FontWeight ToFontWeight(int weight)
        {
            // FromOpenTypeWeight 对超出 1~999 的值会抛异常，越界时回退默认值
            return weight >= 1 && weight <= 999 ? FontWeight.FromOpenTypeWeight(weight) : FontWeights.Normal;
        }
    }

    /// <summary>
    /// WPF 的 FontStyle 同样会被序列化成 {}，本转换器改为存 "Normal"/"Italic"/"Oblique" 字符串，
    /// 旧文件里的 {} / null 回退为 Normal，保证不崩。
    /// </summary>
    public sealed class FontStyleJsonConverter : JsonConverter<FontStyle>
    {
        public override FontStyle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string text = reader.GetString();
                if (string.Equals(text, "Italic", StringComparison.OrdinalIgnoreCase))
                    return FontStyles.Italic;
                if (string.Equals(text, "Oblique", StringComparison.OrdinalIgnoreCase))
                    return FontStyles.Oblique;
                return FontStyles.Normal;
            }

            // 旧版本写入的 {}、null 或其他异常数据：跳过并回退默认字形
            reader.Skip();
            return FontStyles.Normal;
        }

        public override void Write(Utf8JsonWriter writer, FontStyle value, JsonSerializerOptions options)
        {
            string text = value == FontStyles.Italic ? "Italic"
                : value == FontStyles.Oblique ? "Oblique"
                : "Normal";
            writer.WriteStringValue(text);
        }
    }

    public class FontModel
    {
        public System.Drawing.Font Font => !string.IsNullOrEmpty(FontFamily) ? new(FontFamily, FontSize, Style, Unit) : null;
        public float FontSize { get; set; }
        public string FontFamily { get; set; }

        /// <summary>字重（WPF 类型，按 OpenType 数值持久化，默认 Normal=400）</summary>
        [JsonConverter(typeof(FontWeightJsonConverter))]
        public FontWeight FontWeight { get; set; }

        /// <summary>字形（WPF 类型，按 Normal/Italic/Oblique 字符串持久化）</summary>
        [JsonConverter(typeof(FontStyleJsonConverter))]
        public FontStyle FontStyle { get; set; }

        public System.Drawing.FontStyle Style { get; set; }
        public System.Drawing.GraphicsUnit Unit { get; set; }
        public int FontColor { get; set; }
        public string FontBackgroundColor { get; set; }
        public string FontBorderColor { get; set; }
        public string FontBorderWidth { get; set; }

        public FontModel Clone()
        {
            return new FontModel()
            {
                FontSize = FontSize,
                FontFamily = FontFamily,
                FontWeight = FontWeight,
                FontStyle = FontStyle,
                Style = Style,
                Unit = Unit,
                FontColor = FontColor,
                FontBackgroundColor = FontBackgroundColor,
                FontBorderColor = FontBorderColor,
                FontBorderWidth = FontBorderWidth,
            };
        }

        public FontModel()
        {
            FontSize = (float)SystemFonts.MessageFontSize;
            FontFamily = SystemFonts.MessageFontFamily.Source;
            FontStyle = SystemFonts.MessageFontStyle;
            FontWeight = SystemFonts.MessageFontWeight;
        }
    }
}
