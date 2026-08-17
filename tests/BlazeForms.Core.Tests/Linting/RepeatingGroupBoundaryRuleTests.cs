using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Linting;

namespace BlazeForms.Core.Tests.Linting;

/// <summary>
/// FR-04 (PRD §5's "Reference semantics"): a rule may not reach into a repeating group's rows
/// from outside that exact group.
/// </summary>
public sealed class RepeatingGroupBoundaryRuleTests
{
    private static readonly ILintRule Rule = RuleTestHelpers.RuleFor(LintRuleIds.Fr04);

    private static ConditionGroup Refers(string field) => new()
    {
        Conditions = [new Condition { Field = field, Operator = ConditionOperator.Is, Value = "yes" }],
    };

    private static FormNode Repeating(string id, params FormNode[] children) => new()
    {
        Id = id,
        Type = NodeType.Repeating,
        Label = id,
        Children = children,
    };

    [Fact]
    public void AVisibilityRuleOutsideAGroupReferencingAFieldInsideItIsFlaggedAndAnchoredToTheOutsideNode()
    {
        var definition = RuleTestHelpers.Definition(
        [
            Repeating("node-group", new FormNode { Id = "child-a", Type = NodeType.Text, Label = "A" }),
            new FormNode
            {
                Id = "node-outside",
                Type = NodeType.Text,
                Label = "Outside",
                VisibleWhen = Refers("child-a"),
            },
        ]);

        var result = Assert.Single(RuleTestHelpers.Analyze(Rule, definition));

        Assert.Equal(LintRuleIds.Fr04, result.RuleId);
        Assert.Equal(LintSeverity.Blocking, result.Severity);
        Assert.Equal("node-outside", result.NodeId);
        Assert.Contains("child-a", result.Detail, StringComparison.Ordinal);
        Assert.Contains("node-group", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AVisibilityRuleInOneGroupReferencingAFieldInADifferentGroupIsFlaggedAndAnchoredToTheChild()
    {
        var definition = RuleTestHelpers.Definition(
        [
            Repeating(
                "node-group-a",
                new FormNode
                {
                    Id = "child-a1",
                    Type = NodeType.Text,
                    Label = "A1",
                    VisibleWhen = Refers("child-b1"),
                }),
            Repeating("node-group-b", new FormNode { Id = "child-b1", Type = NodeType.Text, Label = "B1" }),
        ]);

        var result = Assert.Single(RuleTestHelpers.Analyze(Rule, definition));

        Assert.Equal("child-a1", result.NodeId);
        Assert.Contains("child-b1", result.Detail, StringComparison.Ordinal);
        Assert.Contains("node-group-b", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ACalculationInsideAGroupReferencingAFieldInADifferentGroupIsFlagged()
    {
        var definition = RuleTestHelpers.Definition(
        [
            Repeating(
                "node-group-a",
                new FormNode
                {
                    Id = "child-total",
                    Type = NodeType.Calc,
                    Label = "Total",
                    Calculation = new CalcExpression
                    {
                        Operation = CalcOperation.Sum,
                        Operands = [new CalcOperand { Field = "child-fee" }],
                    },
                }),
            Repeating("node-group-b", new FormNode { Id = "child-fee", Type = NodeType.Currency, Label = "Fee" }),
        ]);

        var result = Assert.Single(RuleTestHelpers.Analyze(Rule, definition));

        Assert.Equal("child-total", result.NodeId);
        Assert.Contains("child-fee", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AValidationRuleWhoseTargetAndExpressionCrossGroupsIsFlaggedAndAnchoredToTheTarget()
    {
        var definition = RuleTestHelpers.Definition(
        [
            Repeating("node-group-a", new FormNode { Id = "child-a1", Type = NodeType.Text, Label = "A1" }),
            Repeating("node-group-b", new FormNode { Id = "child-b1", Type = NodeType.Text, Label = "B1" }),
        ],
        [
            new ValidationRule
            {
                Target = "child-a1",
                Message = "Enter a value for 'A1'.",
                Expression = Refers("child-b1"),
            },
        ]);

        var result = Assert.Single(RuleTestHelpers.Analyze(Rule, definition));

        Assert.Equal("child-a1", result.NodeId);
        Assert.Contains("child-b1", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AReferenceFromInsideAGroupToAFieldOutsideItIsClean()
    {
        var definition = RuleTestHelpers.Definition(
        [
            new FormNode { Id = "node-outer", Type = NodeType.YesNo, Label = "Outer" },
            Repeating(
                "node-group",
                new FormNode
                {
                    Id = "child-a",
                    Type = NodeType.Text,
                    Label = "A",
                    VisibleWhen = Refers("node-outer"),
                }),
        ]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }

    [Fact]
    public void AReferenceToASiblingInTheSameGroupIsClean()
    {
        var definition = RuleTestHelpers.Definition(
        [
            Repeating(
                "node-group",
                new FormNode { Id = "child-a", Type = NodeType.YesNo, Label = "A" },
                new FormNode
                {
                    Id = "child-b",
                    Type = NodeType.Text,
                    Label = "B",
                    VisibleWhen = Refers("child-a"),
                }),
        ]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }

    [Fact]
    public void AReferenceToTheGroupsOwnIdFromOutsideIsClean()
    {
        // The group's own aggregate value is not "a field inside the group" for FR-04's purpose;
        // referencing the group itself is unrelated to reaching into one specific row.
        var definition = RuleTestHelpers.Definition(
        [
            Repeating("node-group", new FormNode { Id = "child-a", Type = NodeType.Text, Label = "A" }),
            new FormNode
            {
                Id = "node-outside",
                Type = NodeType.Text,
                Label = "Outside",
                VisibleWhen = Refers("node-group"),
            },
        ]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }

    [Fact]
    public void ADanglingReferenceIsNotFlaggedByThisRule()
    {
        // A reference to a field that does not exist at all is FR-03's concern, not FR-04's.
        var definition = RuleTestHelpers.Definition(
        [
            new FormNode
            {
                Id = "node-outside",
                Type = NodeType.Text,
                Label = "Outside",
                VisibleWhen = Refers("node-ghost"),
            },
        ]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }

    [Fact]
    public void LiveReferencesEntirelyOutsideAnyGroupAreClean()
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
        ]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }
}
