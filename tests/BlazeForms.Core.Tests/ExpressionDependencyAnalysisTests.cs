using BlazeForms.Definitions;
using BlazeForms.Expressions;

namespace BlazeForms.Core.Tests;

/// <summary>
/// Covers <see cref="ExpressionDependencyAnalysis"/>: <see cref="ExpressionDependencyAnalysis.ReferencesTo"/>
/// finds every visibility- and validation-rule reference to a field (PRD §6, the delete-protection
/// foundation for PRD §4.1), and <see cref="ExpressionDependencyAnalysis.WouldCreateCycle"/> rejects
/// a candidate <see cref="FormNode.VisibleWhen"/> that would close a cycle in the visibility graph,
/// naming the cycle as an ordered path.
/// </summary>
public sealed class ExpressionDependencyAnalysisTests
{
    private static FormNode Node(string id, ConditionGroup? visibleWhen = null) =>
        new() { Id = id, Type = NodeType.Text, Label = id, VisibleWhen = visibleWhen };

    private static ConditionGroup ReferencesField(params string[] fields) => new()
    {
        Conditions = [.. fields.Select(field => new Condition { Field = field, Operator = ConditionOperator.IsBlank })],
    };

    private static FormNode CalcNode(string id, CalcExpression? calculation = null) =>
        new() { Id = id, Type = NodeType.Calc, Label = id, Calculation = calculation };

    private static CalcExpression Calculates(params string[] fields) => new()
    {
        Operation = CalcOperation.Sum,
        Operands = [.. fields.Select(field => new CalcOperand { Field = field })],
    };

    private static FormDefinition Definition(params FormNode[] nodes) => new()
    {
        Id = "form-under-test",
        Name = "Form under test",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Sections = [new FormSection { Id = "section-1", Nodes = nodes }],
            },
        ],
    };

    // -- WouldCreateCycle -----------------------------------------------------------------

    [Fact]
    public void SelfReferenceIsACycle()
    {
        var definition = Definition(Node("a"));
        var candidate = ReferencesField("a");

        var isCycle = ExpressionDependencyAnalysis.WouldCreateCycle(definition, "a", candidate, out var path);

        Assert.True(isCycle);
        Assert.Equal(["a", "a"], path);
    }

    [Fact]
    public void ADirectTwoNodeCycleIsRejected()
    {
        // b already depends on a; giving a a candidate rule that depends on b closes the loop.
        var definition = Definition(Node("a"), Node("b", ReferencesField("a")));
        var candidate = ReferencesField("b");

        var isCycle = ExpressionDependencyAnalysis.WouldCreateCycle(definition, "a", candidate, out var path);

        Assert.True(isCycle);
        Assert.Equal(["a", "b", "a"], path);
    }

    [Fact]
    public void ATransitiveThreeNodeCycleIsRejectedWithTheFullPath()
    {
        // b already depends on c, and c already depends on a; giving a a candidate rule that
        // depends on b closes a → b → c → a.
        var definition = Definition(
            Node("a"),
            Node("b", ReferencesField("c")),
            Node("c", ReferencesField("a")));
        var candidate = ReferencesField("b");

        var isCycle = ExpressionDependencyAnalysis.WouldCreateCycle(definition, "a", candidate, out var path);

        Assert.True(isCycle);
        Assert.Equal(["a", "b", "c", "a"], path);
    }

    [Fact]
    public void ADiamondSharedDependencyIsNotACycle()
    {
        // a depends on both b and c (the candidate); b and c both depend on d. d is reached by two
        // different paths, but neither one ever leads back to a.
        var definition = Definition(
            Node("a"),
            Node("b", ReferencesField("d")),
            Node("c", ReferencesField("d")),
            Node("d"));
        var candidate = ReferencesField("b", "c");

        var isCycle = ExpressionDependencyAnalysis.WouldCreateCycle(definition, "a", candidate, out var path);

        Assert.False(isCycle);
        Assert.Empty(path);
    }

    [Fact]
    public void ACandidateThatDependsOnNothingNewIsNeverACycle()
    {
        var definition = Definition(Node("a"), Node("b"));
        var candidate = ReferencesField("b");

        var isCycle = ExpressionDependencyAnalysis.WouldCreateCycle(definition, "a", candidate, out var path);

        Assert.False(isCycle);
        Assert.Empty(path);
    }

    // -- ReferencesTo -----------------------------------------------------------------------

    [Fact]
    public void ReferencesToFindsAVisibilityReference()
    {
        var definition = Definition(Node("a"), Node("b", ReferencesField("a")));

        var sites = ExpressionDependencyAnalysis.ReferencesTo(definition, "a");

        var site = Assert.Single(sites);
        Assert.Equal(ReferenceKind.Visibility, site.Kind);
        Assert.Equal("b", site.ReferencingNodeId);
        Assert.Null(site.ReferencingRule);
    }

    [Fact]
    public void ReferencesToFindsAValidationTargetAndAValidationExpressionReference()
    {
        var targetRule = new ValidationRule
        {
            Target = "a",
            Message = "Enter a value for 'a'.",
            Expression = ReferencesField("a"),
        };
        var definition = Definition(Node("a")) with { ValidationRules = [targetRule] };

        var sites = ExpressionDependencyAnalysis.ReferencesTo(definition, "a");

        Assert.Equal(2, sites.Count);
        Assert.Contains(sites, site => site.Kind == ReferenceKind.ValidationTarget && site.ReferencingRule == targetRule);
        Assert.Contains(sites, site => site.Kind == ReferenceKind.ValidationExpression && site.ReferencingRule == targetRule);
    }

    [Fact]
    public void ReferencesToCombinesVisibilityAndValidationReferencesForTheSameField()
    {
        var rule = new ValidationRule
        {
            Target = "b",
            Message = "Enter a value for 'b'.",
            Expression = ReferencesField("y"),
        };
        var definition = Definition(Node("y"), Node("b", ReferencesField("y"))) with { ValidationRules = [rule] };

        var sites = ExpressionDependencyAnalysis.ReferencesTo(definition, "y");

        Assert.Equal(2, sites.Count);
        Assert.Contains(sites, site => site.Kind == ReferenceKind.Visibility && site.ReferencingNodeId == "b");
        Assert.Contains(sites, site => site.Kind == ReferenceKind.ValidationExpression && site.ReferencingRule == rule);
    }

    [Fact]
    public void ReferencesToIsEmptyForAFieldNothingReferences()
    {
        var definition = Definition(Node("a"), Node("b"));

        var sites = ExpressionDependencyAnalysis.ReferencesTo(definition, "a");

        Assert.Empty(sites);
    }

    [Fact]
    public void ReferencesToFindsACalculationReference()
    {
        var definition = Definition(Node("fee"), CalcNode("total", Calculates("fee")));

        var sites = ExpressionDependencyAnalysis.ReferencesTo(definition, "fee");

        var site = Assert.Single(sites);
        Assert.Equal(ReferenceKind.Calculation, site.Kind);
        Assert.Equal("total", site.ReferencingNodeId);
        Assert.Null(site.ReferencingRule);
    }

    // -- WouldCreateCalculationCycle --------------------------------------------------------

    [Fact]
    public void ACalculationSelfReferenceIsACycle()
    {
        var definition = Definition(CalcNode("a"));
        var candidate = Calculates("a");

        var isCycle = ExpressionDependencyAnalysis.WouldCreateCalculationCycle(definition, "a", candidate, out var path);

        Assert.True(isCycle);
        Assert.Equal(["a", "a"], path);
    }

    [Fact]
    public void ATransitiveThreeNodeCalculationCycleIsRejectedWithTheFullPath()
    {
        // b already calculates from c, c already calculates from a; giving a a candidate that
        // calculates from b closes a → b → c → a.
        var definition = Definition(
            CalcNode("a"),
            CalcNode("b", Calculates("c")),
            CalcNode("c", Calculates("a")));
        var candidate = Calculates("b");

        var isCycle = ExpressionDependencyAnalysis.WouldCreateCalculationCycle(definition, "a", candidate, out var path);

        Assert.True(isCycle);
        Assert.Equal(["a", "b", "c", "a"], path);
    }

    [Fact]
    public void ACalculationCandidateThatDependsOnNothingNewIsNotACycle()
    {
        var definition = Definition(CalcNode("a"), Node("b"));
        var candidate = Calculates("b");

        var isCycle = ExpressionDependencyAnalysis.WouldCreateCalculationCycle(definition, "a", candidate, out var path);

        Assert.False(isCycle);
        Assert.Empty(path);
    }

    [Fact]
    public void TheVisibilityAndCalculationGraphsAreIndependent()
    {
        // 'shown' is visible-when it references 'total'; 'total' would calculate from 'shown'. That
        // is a cycle only if the two graphs are conflated — which they must not be. Neither the
        // calculation check nor the visibility check should see a loop.
        var definition = Definition(
            CalcNode("total"),
            Node("shown", ReferencesField("total")));

        var calcCycle = ExpressionDependencyAnalysis.WouldCreateCalculationCycle(definition, "total", Calculates("shown"), out var calcPath);
        var visibilityCycle = ExpressionDependencyAnalysis.WouldCreateCycle(definition, "shown", ReferencesField("total"), out var visibilityPath);

        Assert.False(calcCycle);
        Assert.Empty(calcPath);
        Assert.False(visibilityCycle);
        Assert.Empty(visibilityPath);
    }
}
