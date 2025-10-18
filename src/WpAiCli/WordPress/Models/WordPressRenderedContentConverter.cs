using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpAiCli.WordPress.Models;

public sealed class WordPressRenderedContentConverter : JsonConverter<WordPressRenderedContent>
{
    public override WordPressRenderedContent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => FromString(reader.GetString()),
            JsonTokenType.StartObject => FromElement(JsonDocument.ParseValue(ref reader).RootElement),
            JsonTokenType.StartArray => FromArray(ref reader),
            _ => throw new JsonException("Unsupported JSON token for WordPressRenderedContent"),
        };
    }

    public override void Write(Utf8JsonWriter writer, WordPressRenderedContent value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Raw is not null)
        {
            writer.WriteString("raw", value.Raw);
        }
        if (value.Rendered is not null)
        {
            writer.WriteString("rendered", value.Rendered);
        }
        writer.WriteEndObject();
    }

    private static WordPressRenderedContent FromString(string? value)
        => new() { Raw = value, Rendered = value };

    private static WordPressRenderedContent FromArray(ref Utf8JsonReader reader)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.GetArrayLength() == 0)
        {
            return new WordPressRenderedContent();
        }

        var first = doc.RootElement[0];
        return first.ValueKind switch
        {
            JsonValueKind.Object => FromElement(first),
            JsonValueKind.String => FromString(first.GetString()),
            _ => new WordPressRenderedContent(),
        };
    }

    private static WordPressRenderedContent FromElement(JsonElement element)
    {
        var content = new WordPressRenderedContent();
        if (element.TryGetProperty("rendered", out var rendered))
        {
            content.Rendered = rendered.GetString();
        }
        if (element.TryGetProperty("raw", out var raw))
        {
            content.Raw = raw.GetString();
        }
        if (content.Rendered is null && content.Raw is null && element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            content.Rendered = text;
            content.Raw = text;
        }
        return content;
    }
}
