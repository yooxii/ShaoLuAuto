using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShaoLu.Models
{
    /// <summary>
    /// 内容项包装类，用于支持 WPF DataGrid/ListBox 中字符串的双向绑定编辑
    /// </summary>
    public class ContentItem : ObservableObject
    {
        private string _text = "";

        /// <summary>
        /// 文本内容
        /// </summary>
        public string Text { get => _text; set => SetProperty(ref _text, value); }

        public ContentItem() { }

        public ContentItem(string text)
        {
            Text = text ?? "";
        }

        public override string ToString() => Text;
    }

    /// <summary>
    /// 将 ObservableCollection&lt;ContentItem&gt; 序列化为字符串数组，反序列化时兼容字符串数组格式
    /// </summary>
    public class ContentItemCollectionConverter : JsonConverter<ObservableCollection<ContentItem>>
    {
        public override ObservableCollection<ContentItem> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var collection = new ObservableCollection<ContentItem>();

            if (reader.TokenType == JsonTokenType.Null)
                return collection;

            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Expected start of array for Contents.");

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    break;

                if (reader.TokenType == JsonTokenType.String)
                {
                    collection.Add(new ContentItem(reader.GetString()));
                }
                else if (reader.TokenType == JsonTokenType.StartObject)
                {
                    // 支持对象格式 {"Text": "..."} 以兼容未来扩展
                    string text = "";
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndObject)
                            break;
                        if (reader.TokenType == JsonTokenType.PropertyName && reader.GetString() == "Text")
                        {
                            reader.Read();
                            text = reader.GetString() ?? "";
                        }
                    }
                    collection.Add(new ContentItem(text));
                }
            }

            return collection;
        }

        public override void Write(Utf8JsonWriter writer, ObservableCollection<ContentItem> value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartArray();
            foreach (var item in value)
            {
                writer.WriteStringValue(item?.Text ?? "");
            }
            writer.WriteEndArray();
        }
    }
}
