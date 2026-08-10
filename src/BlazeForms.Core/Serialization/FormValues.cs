using System.Buffers;
using System.Globalization;
using System.Text.Json;

namespace BlazeForms.Serialization;

/// <summary>
/// Converts between the loosely typed answer dictionary the expression engine reads and the
/// JSON-shaped answers a submission envelope or fill draft carries.
/// </summary>
/// <remarks>
/// The conversion is an explicit switch over the answer shapes BlazeForms captures, not
/// reflection: Core stays trim-compatible, and an unexpected host type fails loudly instead of
/// serializing into something the schema does not describe. Round-tripping is lossy for types
/// JSON has no notion of — a <see cref="DateOnly"/> comes back as its ISO-8601 text, which every
/// operator still coerces correctly.
/// </remarks>
public static class FormValues
{
    /// <summary>
    /// Converts answers to their JSON form, ready for a submission envelope or a fill draft.
    /// </summary>
    /// <param name="values">
    /// The answers, keyed by node ID.
    /// </param>
    /// <returns>
    /// The same answers as detached <see cref="JsonElement"/> values.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// An answer is of a type BlazeForms does not capture. Convert it to text, a number, a
    /// boolean, a date, a collection of stored option values, or a
    /// <see cref="JsonElement"/> first.
    /// </exception>
    public static IReadOnlyDictionary<string, JsonElement> ToJsonValues(
        IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var converted = new Dictionary<string, JsonElement>(values.Count, StringComparer.Ordinal);

        foreach (var pair in values)
        {
            converted[pair.Key] = ToJsonElement(pair.Value);
        }

        return converted;
    }

    /// <summary>
    /// Converts JSON answers back into the shapes the expression engine reads.
    /// </summary>
    /// <param name="values">
    /// The answers, keyed by node ID.
    /// </param>
    /// <returns>
    /// The same answers as text, booleans, numbers, or collections of stored option values. A
    /// JSON object is handed back as its <see cref="JsonElement"/>, since the P1 schema captures
    /// no structured answers.
    /// </returns>
    public static IReadOnlyDictionary<string, object?> FromJsonValues(
        IReadOnlyDictionary<string, JsonElement> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var converted = new Dictionary<string, object?>(values.Count, StringComparer.Ordinal);

        foreach (var pair in values)
        {
            converted[pair.Key] = FromJsonElement(pair.Value);
        }

        return converted;
    }

    private static JsonElement ToJsonElement(object? value)
    {
        if (value is JsonElement already)
        {
            return already.Clone();
        }

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, value);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);

        return document.RootElement.Clone();
    }

    private static void Write(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;
            case string text:
                writer.WriteStringValue(text);
                return;
            case bool flag:
                writer.WriteBooleanValue(flag);
                return;
            case int number:
                writer.WriteNumberValue(number);
                return;
            case long number:
                writer.WriteNumberValue(number);
                return;
            case decimal number:
                writer.WriteNumberValue(number);
                return;
            case double number:
                writer.WriteNumberValue(number);
                return;
            case float number:
                writer.WriteNumberValue(number);
                return;
            case DateOnly date:
                writer.WriteStringValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                return;
            case TimeOnly time:
                writer.WriteStringValue(time.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
                return;
            case DateTimeOffset instant:
                writer.WriteStringValue(instant);
                return;
            case DateTime instant:
                writer.WriteStringValue(instant);
                return;
            case IEnumerable<string> selections:
                writer.WriteStartArray();

                foreach (var selection in selections)
                {
                    writer.WriteStringValue(selection);
                }

                writer.WriteEndArray();
                return;
            default:
                throw new NotSupportedException(
                    $"BlazeForms does not capture answers of type '{value.GetType()}'. Convert the answer to text, a number, a boolean, a date, a collection of stored option values, or a JsonElement first.");
        }
    }

    private static object? FromJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetDecimal(out var number) ? number : element.GetDouble(),
        JsonValueKind.Array => FromJsonArray(element),
        _ => element,
    };

    private static object FromJsonArray(JsonElement element)
    {
        var selections = new List<string>(element.GetArrayLength());

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return element;
            }

            selections.Add(item.GetString()!);
        }

        return selections;
    }
}
