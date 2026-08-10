using BlazeForms.Definitions;
using BlazeForms.Expressions;

namespace BlazeForms.Core.Tests;

/// <summary>
/// PRD §6/§9: hidden fields are excluded from the submission payload — absent, not null.
/// </summary>
public sealed class VisibilityEvaluatorTests
{
    private static readonly IReadOnlyDictionary<string, object?> TriggerOn =
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-trigger"] = "yes",
            ["node-detail"] = "Wheelchair lift",
        };

    private static readonly IReadOnlyDictionary<string, object?> TriggerOff =
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-trigger"] = "no",
            ["node-detail"] = "Wheelchair lift",
        };

    private static FormNode RequireNode(FormDefinition definition, string nodeId) =>
        definition.FindNode(nodeId) ?? throw new InvalidOperationException($"Fixture is missing node '{nodeId}'.");

    [Fact]
    public void ANodeWithoutARuleIsAlwaysVisible()
    {
        var node = RequireNode(TestDefinitions.ConditionalDefinition, "node-trigger");

        Assert.True(VisibilityEvaluator.IsVisible(node, TriggerOn));
        Assert.True(VisibilityEvaluator.IsVisible(node, TriggerOff));
    }

    [Fact]
    public void ANodeWithARuleFollowsTheRule()
    {
        var node = RequireNode(TestDefinitions.ConditionalDefinition, "node-detail");

        Assert.True(VisibilityEvaluator.IsVisible(node, TriggerOn));
        Assert.False(VisibilityEvaluator.IsVisible(node, TriggerOff));
    }

    [Fact]
    public void FindNodeReturnsNullForAnUnknownId()
    {
        Assert.Null(TestDefinitions.ConditionalDefinition.FindNode("node-that-never-existed"));
    }

    [Fact]
    public void GetVisibleNodesOmitsHiddenNodesAndKeepsTheRest()
    {
        var visible = VisibilityEvaluator
            .GetVisibleNodes(TestDefinitions.ConditionalDefinition, TriggerOff)
            .Select(node => node.Id)
            .ToList();

        Assert.Equal(["node-trigger", "node-static-heading"], visible);
    }

    [Fact]
    public void HiddenFieldValuesAreAbsentFromTheFilteredPayload()
    {
        var filtered = VisibilityEvaluator.FilterToVisible(TestDefinitions.ConditionalDefinition, TriggerOff);

        Assert.False(filtered.ContainsKey("node-detail"));
        Assert.Equal("no", filtered["node-trigger"]);
        Assert.Single(filtered);
    }

    [Fact]
    public void VisibleFieldValuesSurviveFiltering()
    {
        var filtered = VisibilityEvaluator.FilterToVisible(TestDefinitions.ConditionalDefinition, TriggerOn);

        Assert.Equal(2, filtered.Count);
        Assert.Equal("Wheelchair lift", filtered["node-detail"]);
    }

    [Fact]
    public void FilteringDropsValuesThatBelongToNoInputNode()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-trigger"] = "yes",
            ["node-static-heading"] = "static nodes hold no answer",
            ["node-that-was-deleted"] = "orphan",
        };

        var filtered = VisibilityEvaluator.FilterToVisible(TestDefinitions.ConditionalDefinition, values);

        Assert.Single(filtered);
        Assert.True(filtered.ContainsKey("node-trigger"));
    }

    [Fact]
    public void FilteringHonoursTheRepresentativeDefinitionsConditionalBranch()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-needs-transport"] = "no",
            ["node-bus-route"] = "route-a",
            ["node-grade-level"] = 9m,
        };

        var filtered = VisibilityEvaluator.FilterToVisible(TestDefinitions.RepresentativeDefinition, values);

        Assert.False(filtered.ContainsKey("node-bus-route"));
        Assert.True(filtered.ContainsKey("node-needs-transport"));
        Assert.True(filtered.ContainsKey("node-grade-level"));
    }

    [Fact]
    public void NestedRepeatingChildrenAreReachableFromTheDefinition()
    {
        var node = RequireNode(TestDefinitions.RepresentativeDefinition, "node-sibling-name");

        Assert.Equal(NodeType.Text, node.Type);
    }

    [Fact]
    public void AChainOfRulesSettlesSoAHiddenTriggerCannotLeakItsDependent()
    {
        // a → b → c: b shows when a is "yes", c shows when b is "yes". The respondent answered
        // "no" to a, which hides b — so c must be hidden too, however b's stale answer reads.
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-a"] = "no",
            ["node-b"] = "yes",
            ["node-c"] = "leaked",
        };

        var filtered = VisibilityEvaluator.FilterToVisible(TestDefinitions.ChainedDefinition, values);

        Assert.Equal(["node-a"], filtered.Keys.Order(StringComparer.Ordinal));
        Assert.False(filtered.ContainsKey("node-b"));
        Assert.False(filtered.ContainsKey("node-c"));
    }

    [Fact]
    public void AChainOfRulesKeepsEveryAnswerWhileTheChainHolds()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-a"] = "yes",
            ["node-b"] = "yes",
            ["node-c"] = "kept",
        };

        var filtered = VisibilityEvaluator.FilterToVisible(TestDefinitions.ChainedDefinition, values);

        Assert.Equal(["node-a", "node-b", "node-c"], filtered.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void BreakingTheChainInTheMiddleHidesOnlyWhatDependsOnIt()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-a"] = "yes",
            ["node-b"] = "no",
            ["node-c"] = "leaked",
        };

        var filtered = VisibilityEvaluator.FilterToVisible(TestDefinitions.ChainedDefinition, values);

        Assert.Equal(["node-a", "node-b"], filtered.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AChildOfAHiddenContainerIsHiddenHoweverItsOwnRuleReads()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-has-siblings"] = "no",
            ["node-sibling-name"] = "Grace",
        };

        var visible = VisibilityEvaluator
            .GetVisibleNodes(TestDefinitions.NestedDefinition, values)
            .Select(node => node.Id);

        Assert.Equal(["node-has-siblings"], visible);

        var filtered = VisibilityEvaluator.FilterToVisible(TestDefinitions.NestedDefinition, values);

        Assert.False(filtered.ContainsKey("node-sibling-name"));
    }

    [Fact]
    public void AChildOfAVisibleContainerIsReached()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-has-siblings"] = "yes",
            ["node-sibling-name"] = "Grace",
        };

        var visible = VisibilityEvaluator
            .GetVisibleNodes(TestDefinitions.NestedDefinition, values)
            .Select(node => node.Id);

        Assert.Equal(["node-has-siblings", "node-siblings", "node-sibling-name"], visible);
        Assert.True(VisibilityEvaluator.FilterToVisible(TestDefinitions.NestedDefinition, values)
            .ContainsKey("node-sibling-name"));
    }

    [Fact]
    public void VisibilityEvaluatorRejectsNullArguments()
    {
        var node = new FormNode { Id = "n", Type = NodeType.Text };

        Assert.Throws<ArgumentNullException>(() => VisibilityEvaluator.IsVisible(null!, TriggerOn));
        Assert.Throws<ArgumentNullException>(() => VisibilityEvaluator.IsVisible(node, null!));
        Assert.Throws<ArgumentNullException>(() => VisibilityEvaluator.FilterToVisible(null!, TriggerOn));
        Assert.Throws<ArgumentNullException>(() => VisibilityEvaluator.GetVisibleNodes(null!, TriggerOn));
    }
}
