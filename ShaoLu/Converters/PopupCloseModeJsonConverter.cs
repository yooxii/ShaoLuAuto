using ShaoLu.Models;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShaoLu.Converters
{
    /// <summary>
    /// PopupCloseMode 的 JSON 转换器。
    /// 新格式使用可读的 Flags 字符串（如 "ButtonClick, Timeout"），
    /// 同时兼容旧版单值的数字枚举（0=ButtonClick, 1=Timeout, 2=StepReached）。
    /// </summary>
    public class PopupCloseModeJsonConverter : JsonConverter<PopupCloseMode>
    {
        public override PopupCloseMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    string text = reader.GetString();
                    if (string.IsNullOrWhiteSpace(text))
                        return PopupCloseMode.ButtonClick;
                    return Enum.TryParse<PopupCloseMode>(text, true, out var flags)
                        ? flags
                        : PopupCloseMode.ButtonClick;

                case JsonTokenType.Number:
                    // 旧版单值兼容
                    return reader.GetInt32() switch
                    {
                        0 => PopupCloseMode.ButtonClick,
                        1 => PopupCloseMode.Timeout,
                        2 => PopupCloseMode.StepReached,
                        var v => (PopupCloseMode)v,
                    };

                default:
                    return PopupCloseMode.ButtonClick;
            }
        }

        public override void Write(Utf8JsonWriter writer, PopupCloseMode value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
