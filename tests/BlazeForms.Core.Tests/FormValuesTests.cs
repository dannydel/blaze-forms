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
}
