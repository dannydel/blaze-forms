using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Rules;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="ConditionRow"/> in isolation: its three separately labelled controls (PRD §6,
/// D8's Model A), the Value control's three shapes (hidden for a no-operand operator, an
/// option-value select for a choice field, a text input otherwise), and that every commit raises a
/// well-formed replacement <see cref="Condition"/> through <see cref="ConditionRow.ConditionChanged"/>.
/// </summary>
public sealed class ConditionRowTests : DesignerTestContext
{
    private static readonly IReadOnlyList<FormNode> TextFields =
    [
        new FormNode { Id = "node-a", Type = NodeType.Text, Label = "Field A" },
        new FormNode { Id = "node-b", Type = NodeType.Text, Label = "Field B" },
    ];

    private static readonly IReadOnlyList<FormNode> MixedFields =
    [
        new FormNode { Id = "node-text", Type = NodeType.Text, Label = "Free text" },
        new FormNode
        {
            Id = "node-choice",
            Type = NodeType.Select,
            Label = "Pick one",
            Options =
            [
                new FormOption { Value = "opt-1", Label = "Option one" },
                new FormOption { Value = "opt-2", Label = "Option two" },
            ],
        },
    ];

    [Fact]
    public void RendersThreeSeparatelyLabelledControlsForAValueOperator()
    {
        var condition = new Condition { Field = "node-a", Operator = ConditionOperator.Is, Value = "hi" };
        var cut = Render<ConditionRow>(p => p.Add(c => c.Condition, condition).Add(c => c.Fields, TextFields).Add(c => c.RowNumber, 1));

        var selects = cut.FindAll("select");
        Assert.Equal(2, selects.Count);
        Assert.NotEmpty(cut.FindAll("input[type='text']"));

        var fieldLabel = cut.Find("label[for='" + selects[0].Id + "']");
        Assert.Equal("Condition 1 field", fieldLabel.TextContent);
        var operatorLabel = cut.Find("label[for='" + selects[1].Id + "']");
        Assert.Equal("Condition 1 operator", operatorLabel.TextContent);
    }

    [Theory]
    [InlineData(ConditionOperator.IsTrue)]
    [InlineData(ConditionOperator.IsFalse)]
    [InlineData(ConditionOperator.IsBlank)]
    [InlineData(ConditionOperator.IsNotBlank)]
    public void TheValueControlIsHiddenForEveryNoOperandOperator(ConditionOperator op)
    {
        var condition = new Condition { Field = "node-a", Operator = op };
        var cut = Render<ConditionRow>(p => p.Add(c => c.Condition, condition).Add(c => c.Fields, TextFields));

        Assert.Empty(cut.FindAll("input[type='text']"));
        Assert.Empty(cut.FindAll("label[for*='-value']"));
        Assert.Equal(2, cut.FindAll("select").Count); // Field and Operator only -- no Value control at all
    }

    [Fact]
    public void TheValueControlIsATextInputForANonChoiceField()
    {
        var condition = new Condition { Field = "node-text", Operator = ConditionOperator.Is, Value = "hello" };
        var cut = Render<ConditionRow>(p => p.Add(c => c.Condition, condition).Add(c => c.Fields, MixedFields));

        var valueInput = cut.Find("input[type='text']");
        Assert.Equal("hello", valueInput.GetAttribute("value"));
    }

    [Fact]
    public void TheValueControlIsAnOptionValueSelectForAChoiceField()
    {
        var condition = new Condition { Field = "node-choice", Operator = ConditionOperator.Is, Value = "opt-2" };
        var cut = Render<ConditionRow>(p => p.Add(c => c.Condition, condition).Add(c => c.Fields, MixedFields));

        var selects = cut.FindAll("select");
        Assert.Equal(3, selects.Count); // field, operator, value
        var valueSelect = selects[2];
        var options = valueSelect.QuerySelectorAll("option");

        // A leading blank option plus each of the field's own options, bound to their stored
        // Values and showing their Labels -- never the label as the stored value.
        Assert.Equal(["", "opt-1", "opt-2"], options.Select(o => o.GetAttribute("value")));
        Assert.Equal(["", "Option one", "Option two"], options.Select(o => o.TextContent));
    }

    [Fact]
    public async Task ChangingTheFieldClearsTheValueAndRaisesConditionChanged()
    {
        Condition? raised = null;
        var condition = new Condition { Field = "node-a", Operator = ConditionOperator.Is, Value = "hi" };
        var cut = Render<ConditionRow>(p => p
            .Add(c => c.Condition, condition)
            .Add(c => c.Fields, TextFields)
            .Add(c => c.ConditionChanged, c => raised = c));

        await cut.Find("select").ChangeAsync("node-b");

        Assert.NotNull(raised);
        Assert.Equal("node-b", raised.Field);
        Assert.Null(raised.Value);
        Assert.Equal(ConditionOperator.Is, raised.Operator);
    }

    [Fact]
    public async Task ChangingTheOperatorRaisesConditionChangedWithTheSameFieldAndValue()
    {
        Condition? raised = null;
        var condition = new Condition { Field = "node-a", Operator = ConditionOperator.Is, Value = "hi" };
        var cut = Render<ConditionRow>(p => p
            .Add(c => c.Condition, condition)
            .Add(c => c.Fields, TextFields)
            .Add(c => c.ConditionChanged, c => raised = c));

        await cut.FindAll("select")[1].ChangeAsync(nameof(ConditionOperator.IsBlank));

        Assert.NotNull(raised);
        Assert.Equal(ConditionOperator.IsBlank, raised.Operator);
        Assert.Equal("node-a", raised.Field);
    }

    [Fact]
    public async Task TypingAValueAndBlurringRaisesConditionChanged()
    {
        Condition? raised = null;
        var condition = new Condition { Field = "node-a", Operator = ConditionOperator.Is };
        var cut = Render<ConditionRow>(p => p
            .Add(c => c.Condition, condition)
            .Add(c => c.Fields, TextFields)
            .Add(c => c.ConditionChanged, c => raised = c));

        await cut.Find("input[type='text']").ChangeAsync("Active");

        Assert.NotNull(raised);
        Assert.Equal("Active", raised.Value);
    }

    [Fact]
    public async Task ClickingRemoveRaisesOnRemove()
    {
        var removed = false;
        var condition = new Condition { Field = "node-a", Operator = ConditionOperator.Is };
        var cut = Render<ConditionRow>(p => p
            .Add(c => c.Condition, condition)
            .Add(c => c.Fields, TextFields)
            .Add(c => c.OnRemove, () => removed = true));

        await cut.Find("button").ClickAsync(new MouseEventArgs());

        Assert.True(removed);
    }

    [Fact]
    public void RequestFocusMovesDomFocusToTheFieldSelectOnce()
    {
        var condition = new Condition { Field = "node-a", Operator = ConditionOperator.Is };
        Render<ConditionRow>(p => p
            .Add(c => c.Condition, condition)
            .Add(c => c.Fields, TextFields)
            .Add(c => c.RequestFocus, true));

        JSInterop.VerifyFocusAsyncInvoke(1);
    }

    [Fact]
    public void NoRequestFocusNeverMovesDomFocus()
    {
        var condition = new Condition { Field = "node-a", Operator = ConditionOperator.Is };
        Render<ConditionRow>(p => p
            .Add(c => c.Condition, condition)
            .Add(c => c.Fields, TextFields));

        JSInterop.VerifyNotInvoke("Blazor._internal.domWrapper.focus");
    }
}
