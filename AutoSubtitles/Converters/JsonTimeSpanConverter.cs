using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoSubtitles
{
    public class JsonTimeSpanConverter : JsonConverter<TimeSpan>
    {
        // Чтение JSON в TimeSpan (сработает и для объектов, и для простых строк)
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string timeStr = reader.GetString();
                if (TimeSpan.TryParse(timeStr, out TimeSpan result))
                {
                    return result;
                }
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                // Защита на случай, если время записано в тиках
                return TimeSpan.FromTicks(reader.GetInt64());
            }

            return TimeSpan.Zero; // Возвращаем дефолт, если поле повреждено
        }

        // Запись TimeSpan в JSON в стандартном строковом формате "hh:mm:ss"
        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString(@"hh\:mm\:ss"));
        }
    }
}