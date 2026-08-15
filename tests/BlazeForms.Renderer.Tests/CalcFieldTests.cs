using BlazeForms.Definitions;
using BlazeForms.Fields;
using Bunit;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="CalcField"/>: a read-only, live-region <c>&lt;output&gt;</c> that shows
/// either the display text the renderer's calc evaluation engine formatted, or the node's own
/// placeholder when nothing has been computed (PRD §5, decision log D-E #4). It must never
/// behave like an editable answer.
/// </summary>
public sealed class CalcFieldTests : BunitContext
{
    [Fact]
    public void FallsBackToThePlaceholderWhenNoValueIsGiven()
    {
        var node = TestNodes.Create(NodeType.Calc, label: "Total", placeholder: "Calculated automatically");
        var cut = Render<CalcField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        var output = cut.Find("output");
        Assert.Equal("Calculated automatically", output.TextContent);
        Assert.Equal("f1", output.GetAttribute("id"));
        Assert.Equal("f1", cut.Find("label").GetAttribute("for"));
    }

    [Fact]
    public void RendersAComputedValueInAnOutputElementLabelledByFieldId()
    {
        var node = TestNodes.Create(NodeType.Calc, label: "Total", placeholder: "Calculated automatically");
        var cut = Render<CalcField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.Value, "42"));

        // The renderer's own formatted display text wins over the placeholder once one exists --
        // the placeholder is only ever a fallback for "nothing has been computed yet".
        var output = cut.Find("output");
        Assert.Equal("42", output.TextContent);
        Assert.Equal("f1", output.GetAttribute("id"));
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

        // There is no editable control to raise a change from -- an <output> carries no editable
        // form-control semantics at all, so asserting the negative against the rendered markup is
        // the only thing to prove here.
        Assert.Empty(cut.FindAll("input"));
        Assert.False(raised);
    }

    [Fact]
    public void IsNeverRequired()
    {
        var node = TestNodes.Create(NodeType.Calc, required: true);
        var cut = Render<CalcField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        // <output> has no notion of aria-required to begin with -- this pins that CalcField never
        // adds one itself, the same guarantee the old <input>-based markup gave.
        Assert.Null(cut.Find("output").GetAttribute("aria-required"));
    }
}
