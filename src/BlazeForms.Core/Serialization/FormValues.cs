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
            case RepeatingRows rows:
                WriteRepeatingRows(writer, rows);
                return;
            default:
                throw new NotSupportedException(
                    $"BlazeForms does not capture answers of type '{value.GetType()}'. Convert the answer to text, a number, a boolean, a date, a collection of stored option values, or a JsonElement first.");
        }
    }

    /// <summary>
    /// Writes a <see cref="RepeatingRows"/> answer as a JSON array of
    /// <c>{ "rowId": "...", "values": { ... } }</c> objects — the one canonical, self-describing
    /// shape both the submission envelope and a fill draft carry (repeating-groups-plan.md,
    /// "Resolved decisions" #1). Each row's own values are written through the same
    /// <see cref="Write"/> switch as the top-level answers.
    /// </summary>
    private static void WriteRepeatingRows(Utf8JsonWriter writer, RepeatingRows rows)
    {
        writer.WriteStartArray();

        foreach (var row in rows.Rows)
        {
            writer.WriteStartObject();
            writer.WriteString("rowId", row.RowId);
            writer.WritePropertyName("values");
            writer.WriteStartObject();

            foreach (var pair in row.Values)
            {
                writer.WritePropertyName(pair.Key);
                Write(writer, pair.Value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
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
        // Strict shape recognition: only a non-empty array whose every element is an object with
        // exactly a string "rowId" and an object "values" is a RepeatingRows answer. An empty
        // array is inherently ambiguous with an empty selection list, so it keeps today's
        // behavior (an empty string list) rather than guessing; anything else that fails the
        // shape check — a plain string array, an arbitrary object array — also keeps today's
        // behavior.
        if (element.GetArrayLength() > 0 && TryReadRepeatingRows(element, out var rows))
        {
            return rows;
        }

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

    private static bool TryReadRepeatingRows(JsonElement element, out RepeatingRows rows)
    {
        var parsedRows = new List<RepeatingRow>(element.GetArrayLength());

        foreach (var item in element.EnumerateArray())
        {
            if (!TryReadRepeatingRow(item, out var row))
            {
                rows = RepeatingRows.Empty;
                return false;
            }

            parsedRows.Add(row);
        }

        rows = new RepeatingRows { Rows = parsedRows };
        return true;
    }

    private static bool TryReadRepeatingRow(JsonElement element, out RepeatingRow row)
    {
        row = null!;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string? rowId = null;
        JsonElement? valuesElement = null;
        var propertyCount = 0;

        foreach (var property in element.EnumerateObject())
        {
            propertyCount++;

            if (property.NameEquals("rowId") && property.Value.ValueKind == JsonValueKind.String)
            {
                rowId = property.Value.GetString();
            }
            else if (property.NameEquals("values") && property.Value.ValueKind == JsonValueKind.Object)
            {
                valuesElement = property.Value;
            }
            else
            {
                // An unexpected property, or one of the two expected names carrying the wrong
                // kind of value, means this object is not the strict { rowId, values } shape.
                return false;
            }
        }

        // A blank rowId can never be targeted by the mutators (all guard with
        // ThrowIfNullOrWhiteSpace), so a row carrying one is malformed: reject the strict shape and
        // let the array fall through to an opaque element rather than admitting an unusable row.
        if (propertyCount != 2 || string.IsNullOrWhiteSpace(rowId) || valuesElement is null)
        {
            return false;
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in valuesElement.Value.EnumerateObject())
        {
            values[property.Name] = FromJsonElement(property.Value);
        }

        row = new RepeatingRow { RowId = rowId, Values = values };
        return true;
    }
}
