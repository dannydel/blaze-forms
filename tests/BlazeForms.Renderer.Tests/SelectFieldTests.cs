using BlazeForms.Definitions;
using BlazeForms.Fields;
using Bunit;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="SelectField"/>: renders each option's <see cref="FormOption.Label"/> but
/// stores and emits its <see cref="FormOption.Value"/> (AGENTS.md invariant #5).
/// </summary>
public sealed class SelectFieldTests : BunitContext
{
    private static readonly FormOption[] Options =
    [
        new FormOption { Value = "ca", Label = "California" },
        new FormOption { Value = "ny", Label = "New York" },
    ];

    [Fact]
    public void RendersOptionLabelsWithAPlaceholderBlankOption()
    {
        var node = TestNodes.Create(NodeType.Select, label: "State", placeholder: "Choose a state", options: Options);
        var cut = Render<SelectField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        var optionElements = cut.FindAll("option");
        Assert.Equal(3, optionElements.Count);
        Assert.Equal("Choose a state", optionElements[0].TextContent);
        Assert.Equal("", optionElements[0].GetAttribute("value"));
        Assert.Equal("California", optionElements[1].TextContent);
        Assert.Equal("ca", optionElements[1].GetAttribute("value"));
        Assert.Equal("New York", optionElements[2].TextContent);
        Assert.Equal("ny", optionElements[2].GetAttribute("value"));
    }

    [Fact]
    public void SelectingAnOptionRaisesValueChangedWithItsStoredValueNotItsLabel()
    {
        object? captured = null;
        var node = TestNodes.Create(NodeType.Select, options: Options);
        var cut = Render<SelectField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.ValueChanged, v => captured = v));

        cut.Find("select").Change("ny");

        Assert.Equal("ny", captured);
    }

    [Fact]
    public void LabelAssociatesToFieldId()
    {
        var node = TestNodes.Create(NodeType.Select, label: "State", options: Options);
        var cut = Render<SelectField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        Assert.Equal("f1", cut.Find("label").GetAttribute("for"));
        Assert.Equal("f1", cut.Find("select").GetAttribute("id"));
    }
}
