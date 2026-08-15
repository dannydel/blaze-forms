using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Linting;

namespace BlazeForms.Core.Tests.Linting;

/// <summary>
/// CALC-01 (PRD §8): a calc node with no calculation renders as an empty read-only field, which is
/// an advisory rather than a blocking issue.
/// </summary>
public sealed class CalcMissingExpressionRuleTests
{
    private static readonly ILintRule Rule = RuleTestHelpers.RuleFor(LintRuleIds.Calc01);

    [Fact]
    public void ACalcNodeWithNoCalculationIsFlaggedAsAdvisoryAndAnchoredToItself()
    {
        var definition = RuleTestHelpers.Definition(
        [
            new FormNode { Id = "node-fee", Type = NodeType.Currency, Label = "Fee" },
            new FormNode { Id = "node-total", Type = NodeType.Calc, Label = "Total" },
        ]);

        var result = Assert.Single(RuleTestHelpers.Analyze(Rule, definition));

        Assert.Equal(LintRuleIds.Calc01, result.RuleId);
        Assert.Equal(LintSeverity.Advisory, result.Severity);
        Assert.Equal("node-total", result.NodeId);
    }

    [Fact]
    public void ACalcNodeThatCarriesACalculationIsClean()
    {
        var definition = RuleTestHelpers.Definition(
        [
            new FormNode { Id = "node-fee", Type = NodeType.Currency, Label = "Fee" },
            new FormNode
            {
                Id = "node-total",
                Type = NodeType.Calc,
                Label = "Total",
                Calculation = new CalcExpression
                {
                    Operation = CalcOperation.Sum,
                    Operands = [new CalcOperand { Field = "node-fee" }, new CalcOperand { Number = 50m }],
                },
            },
        ]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }

    [Fact]
    public void ANonCalcNodeIsNeverFlagged()
    {
        var definition = RuleTestHelpers.Definition(
            [new FormNode { Id = "node-text", Type = NodeType.Text, Label = "Text" }]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }
}
