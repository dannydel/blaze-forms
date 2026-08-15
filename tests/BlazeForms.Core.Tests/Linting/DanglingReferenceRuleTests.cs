using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Linting;

namespace BlazeForms.Core.Tests.Linting;

/// <summary>
/// FR-03 (PRD §8): visibility and validation rules may not reference a field that no longer
/// exists.
/// </summary>
public sealed class DanglingReferenceRuleTests
{
    private static readonly ILintRule Rule = RuleTestHelpers.RuleFor(LintRuleIds.Fr03);

    private static ConditionGroup Refers(string field) => new()
    {
        Conditions = [new Condition { Field = field, Operator = ConditionOperator.Is, Value = "yes" }],
    };

    [Fact]
    public void AVisibilityRuleReferencingAMissingFieldIsFlaggedAndAnchoredToItsOwner()
    {
        var definition = RuleTestHelpers.Definition(
        [
            new FormNode { Id = "node-trigger", Type = NodeType.YesNo, Label = "Trigger" },
            new FormNode
            {
                Id = "node-detail",
                Type = NodeType.Text,
                Label = "Detail",
                VisibleWhen = Refers("node-ghost"),
            },
        ]);

        var result = Assert.Single(RuleTestHelpers.Analyze(Rule, definition));

        Assert.Equal(LintRuleIds.Fr03, result.RuleId);
        Assert.Equal(LintSeverity.Blocking, result.Severity);
        Assert.Equal("node-detail", result.NodeId);
        Assert.Contains("node-ghost", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AValidationRuleTargetingAMissingFieldIsFlaggedWithNoAnchor()
    {
        var definition = RuleTestHelpers.Definition(
            [new FormNode { Id = "node-a", Type = NodeType.Text, Label = "A" }],
            [
                new ValidationRule
                {
                    Target = "node-gone",
                    Message = "Fix it.",
                    Expression = Refers("node-a"),
                },
            ]);

        var result = Assert.Single(RuleTestHelpers.Analyze(Rule, definition));

        Assert.Null(result.NodeId);
        Assert.Contains("node-gone", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ACalculationReferencingAMissingFieldIsFlaggedAndAnchoredToItsOwner()
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
                    Operands = [new CalcOperand { Field = "node-fee" }, new CalcOperand { Field = "node-ghost" }],
                },
            },
        ]);

        var result = Assert.Single(RuleTestHelpers.Analyze(Rule, definition));

        Assert.Equal(LintRuleIds.Fr03, result.RuleId);
        Assert.Equal(LintSeverity.Blocking, result.Severity);
        Assert.Equal("node-total", result.NodeId);
        Assert.Contains("node-ghost", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveReferencesAreClean()
    {
        var definition = RuleTestHelpers.Definition(
            [
                new FormNode { Id = "node-trigger", Type = NodeType.YesNo, Label = "Trigger" },
                new FormNode
                {
                    Id = "node-detail",
                    Type = NodeType.Text,
                    Label = "Detail",
                    VisibleWhen = Refers("node-trigger"),
                },
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
            ],
            [
                new ValidationRule
                {
                    Target = "node-detail",
                    Message = "Fix it.",
                    Expression = Refers("node-trigger"),
                },
            ]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }
}
