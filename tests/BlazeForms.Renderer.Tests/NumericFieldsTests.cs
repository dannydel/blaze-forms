using BlazeForms.Definitions;
using BlazeForms.Fields;
using Bunit;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="NumberField"/> and <see cref="CurrencyField"/>: both store
/// <see cref="decimal"/>?, both render <c>type="number"</c>/<c>inputmode="decimal"</c>, and
/// <see cref="CurrencyField"/> additionally pins its <c>step="0.01"</c>.
/// </summary>
public sealed class NumericFieldsTests : BunitContext
{
    [Fact]
    public void NumberFieldRendersNumberTypeAndDecimalInputmodeWithMinMax()
    {
        var node = TestNodes.Create(NodeType.Number, label: "Quantity", min: 1, max: 10);
        var cut = Render<NumberField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        var input = cut.Find("input");
        Assert.Equal("number", input.GetAttribute("type"));
        Assert.Equal("decimal", input.GetAttribute("inputmode"));
        Assert.Equal("1", input.GetAttribute("min"));
        Assert.Equal("10", input.GetAttribute("max"));
    }

    [Fact]
    public void NumberFieldTypingANumberRaisesValueChangedAsDecimal()
    {
        object? captured = null;
        var node = TestNodes.Create(NodeType.Number);
        var cut = Render<NumberField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.ValueChanged, v => captured = v));

        cut.Find("input").Input("42.5");

        Assert.Equal(42.5m, captured);
    }

    [Fact]
    public void CurrencyFieldRendersTwoDecimalStep()
    {
        var node = TestNodes.Create(NodeType.Currency, label: "Amount");
        var cut = Render<CurrencyField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        var input = cut.Find("input");
        Assert.Equal("number", input.GetAttribute("type"));
        Assert.Equal("decimal", input.GetAttribute("inputmode"));
        Assert.Equal("0.01", input.GetAttribute("step"));
    }

    [Fact]
    public void CurrencyFieldTypingAnAmountRaisesValueChangedAsDecimal()
    {
        object? captured = null;
        var node = TestNodes.Create(NodeType.Currency);
        var cut = Render<CurrencyField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.ValueChanged, v => captured = v));

        cut.Find("input").Input("19.99");

        Assert.Equal(19.99m, captured);
    }
}
