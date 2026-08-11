using BlazeForms.Definitions;
using BlazeForms.Fields;
using Bunit;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="DateField"/> (a single <see cref="DateOnly"/>?) and
/// <see cref="DateRangeField"/> (a two-element ISO-date <see cref="string"/> array).
/// </summary>
public sealed class DateFieldsTests : RendererTestContext
{
    [Fact]
    public void DateFieldRendersADateInput()
    {
        var node = TestNodes.Create(NodeType.Date, label: "Date of birth", required: true);
        var cut = Render<DateField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        var input = cut.Find("input");
        Assert.Equal("date", input.GetAttribute("type"));
        Assert.Equal("true", input.GetAttribute("aria-required"));
        Assert.Equal("f1", cut.Find("label").GetAttribute("for"));
    }

    [Fact]
    public void DateFieldPickingADateRaisesValueChangedAsDateOnly()
    {
        object? captured = null;
        var node = TestNodes.Create(NodeType.Date);
        var cut = Render<DateField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.ValueChanged, v => captured = v));

        cut.Find("input").Change("2026-03-15");

        Assert.Equal(new DateOnly(2026, 3, 15), captured);
    }

    [Fact]
    public void DateRangeFieldRendersAFieldsetWithTwoLabelledDateInputs()
    {
        var node = TestNodes.Create(NodeType.DateRange, label: "Coverage period", required: true);
        var cut = Render<DateRangeField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        Assert.NotEmpty(cut.FindAll("fieldset"));
        Assert.Equal("Coverage period", cut.Find("legend").TextContent);

        var inputs = cut.FindAll("input[type='date']");
        Assert.Equal(2, inputs.Count);
        Assert.All(inputs, input => Assert.Equal("true", input.GetAttribute("aria-required")));

        Assert.Equal("f1-start", cut.Find("input#f1-start").GetAttribute("id"));
        Assert.Equal("f1-end", cut.Find("input#f1-end").GetAttribute("id"));
    }

    [Fact]
    public void DateRangeFieldSettingBothSidesRaisesValueChangedWithBothIsoDates()
    {
        object? captured = null;
        var node = TestNodes.Create(NodeType.DateRange);
        var cut = Render<DateRangeField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.ValueChanged, v => captured = v));

        cut.Find("input#f1-start").Change("2026-01-01");

        var afterStart = Assert.IsType<string[]>(captured);
        Assert.Equal(["2026-01-01", ""], afterStart);
    }

    [Fact]
    public void DateRangeFieldClearingBothSidesRaisesValueChangedWithNull()
    {
        object? captured = "unset";
        var node = TestNodes.Create(NodeType.DateRange);
        var initialValue = new[] { "2026-01-01", "" };
        var cut = Render<DateRangeField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.Value, initialValue)
            .Add(f => f.ValueChanged, v => captured = v));

        cut.Find("input#f1-start").Change("");

        Assert.Null(captured);
    }
}
