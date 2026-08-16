using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Rules;
using Bunit;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="CalcOperandRow"/> in isolation, most importantly its own sticky Kind
/// tracking (code review fix #1): the Kind select is the only control that is ever allowed to
/// change which of <see cref="CalcOperand"/>'s three shapes this row shows, so clearing a
/// Number-kind row's own input to retype it must never silently flip the row over to a Field
/// select just because the operand momentarily has nothing set.
/// </summary>
public sealed class CalcOperandRowTests : DesignerTestContext
{
    private static readonly IReadOnlyList<FormNode> NumericFields =
    [
        new FormNode { Id = "node-a", Type = NodeType.Number, Label = "Field A" },
    ];

    [Fact]
    public async Task ClearingANumberOperandsValueKeepsTheRowInNumberKind()
    {
        CalcOperand? updated = null;
        var operand = new CalcOperand { Number = 12m };
        var cut = Render<CalcOperandRow>(p => p
            .Add(c => c.Operand, operand)
            .Add(c => c.Fields, NumericFields)
            .Add(c => c.RowNumber, 1)
            .Add(c => c.OperandChanged, o => updated = o));

        // Number kind renders exactly one select (the Kind select itself -- no Field select).
        Assert.Single(cut.FindAll("select"));
        Assert.NotEmpty(cut.FindAll("input[type='number']"));

        await cut.Find("input[type='number']").ChangeAsync(string.Empty);

        Assert.NotNull(updated);
        Assert.Null(updated!.Number);
        Assert.Null(updated.Field);
        Assert.Null(updated.Function);

        // Simulate the host editor folding the commit back into this row's own Operand parameter
        // -- exactly what CalculationEditor's own working-state operand list does on every commit.
        cut.Render(p => p.Add(c => c.Operand, updated));

        // Still Number kind -- one select (Kind) and the number input, never a Field select --
        // which is what a naive "derive Kind purely from the operand's own current shape" would
        // have produced for a now fully-blank operand.
        Assert.Single(cut.FindAll("select"));
        Assert.NotEmpty(cut.FindAll("input[type='number']"));
    }

    [Fact]
    public async Task RetypingANumberAfterClearingItStillCommitsAsANumberOperand()
    {
        CalcOperand? updated = null;
        var operand = new CalcOperand { Number = 12m };
        var cut = Render<CalcOperandRow>(p => p
            .Add(c => c.Operand, operand)
            .Add(c => c.Fields, NumericFields)
            .Add(c => c.RowNumber, 1)
            .Add(c => c.OperandChanged, o => updated = o));

        await cut.Find("input[type='number']").ChangeAsync(string.Empty);
        cut.Render(p => p.Add(c => c.Operand, updated!));

        await cut.Find("input[type='number']").ChangeAsync("7");

        Assert.Equal(7m, updated!.Number);
        Assert.Null(updated.Field);
    }

    [Fact]
    public void SelectingTodayFromFieldKindShowsTheTodayNoteInstead()
    {
        var operand = new CalcOperand { Field = "node-a" };
        var cut = Render<CalcOperandRow>(p => p
            .Add(c => c.Operand, operand)
            .Add(c => c.Fields, NumericFields)
            .Add(c => c.RowNumber, 1));

        // Field kind shows two selects (Kind, Field).
        Assert.Equal(2, cut.FindAll("select").Count);
        Assert.Empty(cut.FindAll("p.bf-calc-operand-row__today-note"));
    }
}
