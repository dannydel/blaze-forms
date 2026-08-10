using BlazeForms.Definitions;
using BlazeForms.Fields;
using Bunit;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="CalcField"/>: the P1 read-only placeholder for the P2 calc evaluation
/// engine (PRD §5). It must never behave like an editable answer.
/// </summary>
public sealed class CalcFieldTests : BunitContext
{
    [Fact]
    public void RendersAReadOnlyDisabledInputLabelledByFieldId()
    {
        var node = TestNodes.Create(NodeType.Calc, label: "Total", placeholder: "Calculated automatically");
        var cut = Render<CalcField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        var input = cut.Find("input");
        Assert.NotNull(input.GetAttribute("readonly"));
        Assert.NotNull(input.GetAttribute("disabled"));
        Assert.Equal("Calculated automatically", input.GetAttribute("value"));
        Assert.Equal("f1", cut.Find("label").GetAttribute("for"));
    }

    [Fact]
    public void NeverRaisesValueChangedRegardlessOfWhatValueItIsGiven()
    {
        var raised = false;
        var node = TestNodes.Create(NodeType.Calc);
        var cut = Render<CalcField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.Value, "42")
            .Add(f => f.ValueChanged, _ => raised = true));

        // There is no editable control to raise a change from -- assert the negative directly
        // against the rendered markup rather than attempting (and failing) to interact with one.
        Assert.Empty(cut.FindAll("input:not([readonly])"));
        Assert.False(raised);
    }

    [Fact]
    public void IsNeverRequired()
    {
        var node = TestNodes.Create(NodeType.Calc, required: true);
        var cut = Render<CalcField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        Assert.Null(cut.Find("input").GetAttribute("aria-required"));
    }
}
