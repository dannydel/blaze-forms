using System.Text.Json;
using BlazeForms.Definitions;
using BlazeForms.Serialization;

namespace BlazeForms.Core.Tests;

/// <summary>
/// Bridges the loosely typed answer dictionary the expression engine consumes and the
/// JSON-shaped answers the submission envelope carries. Conversion is an explicit switch —
/// Core stays trim-compatible, so no reflection-based serialization is involved.
/// </summary>
public sealed class FormValuesTests
{
    [Fact]
    public void SupportedAnswerTypesRoundTrip()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["text"] = "Ada",
            ["flag"] = true,
            ["count"] = 9m,
            ["missing"] = null,
            ["date"] = new DateOnly(2016, 4, 2),
            ["selections"] = new[] { "lift", "aide" },
        };

        var restored = FormValues.FromJsonValues(FormValues.ToJsonValues(values));

        Assert.Equal("Ada", restored["text"]);
        Assert.Equal(true, restored["flag"]);
        Assert.Equal(9m, restored["count"]);
        Assert.Null(restored["missing"]);
        Assert.Equal("2016-04-02", restored["date"]);
        Assert.Equal(["lift", "aide"], Assert.IsAssignableFrom<IReadOnlyList<string>>(restored["selections"]));
    }

    [Fact]
    public void ConvertedAnswersStayUsableByTheExpressionEngine()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-trigger"] = "yes",
            ["node-detail"] = "Wheelchair lift",
        };

        var restored = FormValues.FromJsonValues(FormValues.ToJsonValues(values));

        Assert.True(BlazeForms.Expressions.VisibilityEvaluator.IsVisible(
            TestDefinitions.ConditionalDefinition.FindNode("node-detail")!,
            restored));
    }

    [Fact]
    public void NumbersKeepTheirPrecision()
    {
        var json = FormValues.ToJsonValues(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["fee"] = 1234.56m,
        });

        Assert.Equal(1234.56m, json["fee"].GetDecimal());
    }

    [Fact]
    public void AnUnsupportedAnswerTypeIsRejectedRatherThanReflectedOver()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["odd"] = new Uri("https://example.gov"),
        };

        Assert.Throws<NotSupportedException>(() => FormValues.ToJsonValues(values));
    }

    [Fact]
    public void JsonElementAnswersArePassedThrough()
    {
        using var document = JsonDocument.Parse("""{"nested":true}""");
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["raw"] = document.RootElement.Clone(),
        };

        var converted = FormValues.ToJsonValues(values);

        Assert.Equal(JsonValueKind.Object, converted["raw"].ValueKind);
    }

    [Fact]
    public void ConversionRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => FormValues.ToJsonValues(null!));
        Assert.Throws<ArgumentNullException>(() => FormValues.FromJsonValues(null!));
    }

    // ---- RepeatingRows: write shape, strict read recognition, round-trip ----

    [Fact]
    public void ARepeatingRowsAnswerRoundTripsIncludingNestedDatesAndChoiceLists()
    {
        var rows = RepeatingRows.Empty
            .AddRow()
            .AddRow();
        var firstRowId = rows.Rows[0].RowId;
        var secondRowId = rows.Rows[1].RowId;

        var accommodations = new[] { "lift", "aide" };
        rows = rows
            .SetValue(firstRowId, "child-name", "Ada")
            .SetValue(firstRowId, "child-dob", new DateOnly(2016, 4, 2))
            .SetValue(firstRowId, "child-accommodations", accommodations)
            .SetValue(secondRowId, "child-name", "Grace");

        var values = new Dictionary<string, object?>(StringComparer.Ordinal) { ["node-siblings"] = rows };

        var restored = FormValues.FromJsonValues(FormValues.ToJsonValues(values));

        var restoredRows = Assert.IsType<RepeatingRows>(restored["node-siblings"]);
        Assert.Equal(2, restoredRows.Rows.Count);

        var restoredFirst = Assert.Single(restoredRows.Rows, row => row.RowId == firstRowId);
        Assert.Equal("Ada", restoredFirst.Values["child-name"]);
        Assert.Equal("2016-04-02", restoredFirst.Values["child-dob"]);
        Assert.Equal(["lift", "aide"], Assert.IsAssignableFrom<IReadOnlyList<string>>(restoredFirst.Values["child-accommodations"]));

        var restoredSecond = Assert.Single(restoredRows.Rows, row => row.RowId == secondRowId);
        Assert.Equal("Grace", restoredSecond.Values["child-name"]);
    }

    [Fact]
    public void AnEmptyRepeatingRowsAnswerRoundTripsAsAnEmptyList()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal) { ["node-siblings"] = RepeatingRows.Empty };

        var restored = FormValues.FromJsonValues(FormValues.ToJsonValues(values));

        // An empty array is inherently ambiguous with an empty selection list; today's behavior
        // (an empty string list) is kept rather than guessing at RepeatingRows.
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<string>>(restored["node-siblings"]));
    }

    [Fact]
    public void APlainStringArrayIsNeverMisclassifiedAsRepeatingRows()
    {
        using var document = JsonDocument.Parse("""["lift","aide"]""");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["node-accommodations"] = document.RootElement.Clone(),
        };

        var restored = FormValues.FromJsonValues(values);

        Assert.Equal(["lift", "aide"], Assert.IsAssignableFrom<IReadOnlyList<string>>(restored["node-accommodations"]));
    }

    [Fact]
    public void AnArrayOfArbitraryObjectsIsNeverMisclassifiedAsRepeatingRowsAndStaysOpaque()
    {
        using var document = JsonDocument.Parse("""[{"foo":"bar"},{"foo":"baz"}]""");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["node-odd"] = document.RootElement.Clone(),
        };

        var restored = FormValues.FromJsonValues(values);

        Assert.IsType<JsonElement>(restored["node-odd"]);
    }

    [Fact]
    public void AnArrayOfObjectsMissingTheValuesPropertyIsNeverMisclassifiedAsRepeatingRows()
    {
        using var document = JsonDocument.Parse("""[{"rowId":"row-1"}]""");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["node-odd"] = document.RootElement.Clone(),
        };

        var restored = FormValues.FromJsonValues(values);

        Assert.IsType<JsonElement>(restored["node-odd"]);
    }

    [Fact]
    public void AnArrayOfObjectsWithAnExtraPropertyIsNeverMisclassifiedAsRepeatingRows()
    {
        using var document = JsonDocument.Parse("""[{"rowId":"row-1","values":{},"extra":true}]""");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["node-odd"] = document.RootElement.Clone(),
        };

        var restored = FormValues.FromJsonValues(values);

        Assert.IsType<JsonElement>(restored["node-odd"]);
    }

    [Fact]
    public void AnArrayWhoseRowHasABlankRowIdIsNeverMisclassifiedAsRepeatingRows()
    {
        // A blank rowId can never be targeted by the mutators, so a row carrying one is malformed;
        // the array must stay opaque rather than admit an unusable row.
        using var document = JsonDocument.Parse("""[{"rowId":"   ","values":{}}]""");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["node-odd"] = document.RootElement.Clone(),
        };

        var restored = FormValues.FromJsonValues(values);

        Assert.IsType<JsonElement>(restored["node-odd"]);
    }

    [Fact]
    public void ARepeatingRowsAnswerSerializesToThePinnedEnvelopeShape()
    {
        var rows = RepeatingRows.Empty.AddRow();
        var rowId = rows.Rows[0].RowId;
        rows = rows.SetValue(rowId, "child-name", "Ada").SetValue(rowId, "child-fee", 12.5m);

        var json = FormValues.ToJsonValues(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-siblings"] = rows,
        });

        var element = json["node-siblings"];

        Assert.Equal(JsonValueKind.Array, element.ValueKind);
        Assert.Equal(1, element.GetArrayLength());

        var rowElement = element[0];
        Assert.Equal(JsonValueKind.Object, rowElement.ValueKind);
        Assert.Equal(rowId, rowElement.GetProperty("rowId").GetString());

        var rowValues = rowElement.GetProperty("values");
        Assert.Equal(JsonValueKind.Object, rowValues.ValueKind);
        Assert.Equal("Ada", rowValues.GetProperty("child-name").GetString());
        Assert.Equal(12.5m, rowValues.GetProperty("child-fee").GetDecimal());
    }
}
