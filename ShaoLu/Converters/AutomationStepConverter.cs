using ShaoLu.Models;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShaoLu.Converters
{
    /// <summary>
    /// 支持多态序列化的转换器
    /// 适用于基类 AutomationStepBase 及其派生类
    /// </summary>
    public class AutomationStepBaseJsonConverter : JsonConverter<AutomationStepBase>
    {
        public override AutomationStepBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // 1. 读取 JSON 为 JsonDocument 以便多次解析
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            // 2. 获取类型标识符 (假设 JSON 中有一个 "Type" 字段表示 StepType)
            if (!root.TryGetProperty("Type", out var typeProperty))
            {
                throw new JsonException("Missing 'Type' property for polymorphic deserialization.");
            }

            // 3. 根据 Type 字段的值决定实例化哪个子类
            // 注意：这里假设 StepType 是枚举，且枚举名称与类名有对应关系，或者你需要手动映射
            StepType stepType;

            // 3. 兼容处理：判断 Type 字段是字符串还是数字
            if (typeProperty.ValueKind == JsonValueKind.String)
            {
                var stepTypeValue = typeProperty.GetString();
                if (string.IsNullOrEmpty(stepTypeValue))
                {
                    throw new JsonException("Invalid 'Type' property.");
                }

                // 尝试解析字符串枚举 (忽略大小写)
                if (!Enum.TryParse<StepType>(stepTypeValue, true, out stepType))
                {
                    throw new JsonException($"Unknown StepType string: {stepTypeValue}");
                }
            }
            else if (typeProperty.ValueKind == JsonValueKind.Number)
            {
                // 尝试解析数字枚举
                if (!Enum.TryParse<StepType>(typeProperty.GetInt32().ToString(), out stepType))
                {
                    // 或者直接转换: (StepType)typeProperty.GetInt32() 
                    // 但建议先检查值是否在枚举范围内，这里简单处理：
                    int val = typeProperty.GetInt32();
                    if (Enum.IsDefined(typeof(StepType), val))
                    {
                        stepType = (StepType)val;
                    }
                    else
                    {
                        throw new JsonException($"Unknown StepType number: {val}");
                    }
                }
            }
            else
            {
                throw new JsonException($"Unsupported 'Type' property format: {typeProperty.ValueKind}");
            }

            // 4. 根据枚举类型创建具体的对象并反序列化
            Type concreteType = stepType switch
            {
                // 请确保这里的映射与你实际的类名一致
                StepType.ClickImage => typeof(ClickImageStep),
                StepType.TypeText => typeof(TypeTextStep),
                // 添加其他映射...
                _ => throw new JsonException($"No mapping found for StepType: {stepType}")
            };

            // 5. 将 JsonElement 重新转换为具体的对象
            var jsonText = root.GetRawText();

            // 注意：为了防止递归调用当前转换器，最好使用一个不包含当前转换器的 options
            // 但如果 concreteType 是具体子类且没有注册全局转换器，直接传 options 通常也是安全的
            // 为了更稳健，可以克隆 options 并移除当前转换器（如果需要）
            return (AutomationStepBase)JsonSerializer.Deserialize(jsonText, concreteType, options);
        }

        public override void Write(Utf8JsonWriter writer, AutomationStepBase value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            // 1. 获取实际运行时类型
            Type actualType = value.GetType();

            // 2. 序列化实际对象的所有属性（包括派生类属性）
            // 使用 JsonSerializer.Serialize 直接序列化具体对象
            // 这会包含派生类的所有公共属性

            // 注意：为了避免无限递归，如果全局注册了这个转换器，我们需要确保
            // 内部序列化时不使用这个转换器处理基类，或者依赖默认行为。
            // 最简单的方法是让 System.Text.Json 默认处理具体类型。

            // 如果具体类型没有自定义转换器，直接序列化即可
            JsonSerializer.Serialize(writer, value, actualType, options);
        }
    }
}