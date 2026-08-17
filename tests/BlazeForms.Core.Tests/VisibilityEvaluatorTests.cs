using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Serialization;

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
        var rows = RepeatingRows.Empty.AddRow();
        rows = rows.SetValue(rows.Rows[0].RowId, "node-sibling-name", "Grace");

        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-has-siblings"] = "no",
            ["node-siblings"] = rows,
        };

        var visible = VisibilityEvaluator
            .GetVisibleNodes(TestDefinitions.NestedDefinition, values)
            .Select(node => node.Id);

        Assert.Equal(["node-has-siblings"], visible);

        var filtered = VisibilityEvaluator.FilterToVisible(TestDefinitions.NestedDefinition, values);

        // The hidden group's whole RepeatingRows value drops -- not just the child's answer.
        Assert.False(filtered.ContainsKey("node-siblings"));
    }

    [Fact]
    public void AChildOfAVisibleContainerIsReached()
    {
        var rows = RepeatingRows.Empty.AddRow();
        rows = rows.SetValue(rows.Rows[0].RowId, "node-sibling-name", "Grace");

        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-has-siblings"] = "yes",
            ["node-siblings"] = rows,
        };

        var visible = VisibilityEvaluator
            .GetVisibleNodes(TestDefinitions.NestedDefinition, values)
            .Select(node => node.Id);

        // GetVisibleNodes reaches the group itself but not its children -- those are scoped per
        // row, not to these flat answers (see GetVisibleNodesDoesNotDescendIntoARepeatingGroupsChildren).
        Assert.Equal(["node-has-siblings", "node-siblings"], visible);

        var filtered = VisibilityEvaluator.FilterToVisible(TestDefinitions.NestedDefinition, values);
        var filteredRows = Assert.IsType<RepeatingRows>(filtered["node-siblings"]);

        Assert.Equal("Grace", Assert.Single(filteredRows.Rows).Values["node-sibling-name"]);
    }

    [Fact]
    public void GetVisibleNodesDoesNotDescendIntoARepeatingGroupsChildren()
    {
        // A deliberate behavior change (repeating-groups-plan.md): a repeating group's children
        // are scoped per row via RowScope, never flat, so the whole-definition walk stops at the
        // group itself even while it is visible. Use GetVisibleChildIds once a row is in hand.
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-has-siblings"] = "yes",
        };

        var visible = VisibilityEvaluator
            .GetVisibleNodes(TestDefinitions.NestedDefinition, values)
            .Select(node => node.Id);

        Assert.Equal(["node-has-siblings", "node-siblings"], visible);
    }

    // ---- Row-scoped visibility (A3): within-row chains, row -> outer references, hidden groups ----

    private static ConditionGroup ShownWhen(string field) => new()
    {
        Conditions = [new Condition { Field = field, Operator = ConditionOperator.Is, Value = "yes" }],
    };

    /// <summary>
    /// A repeating group ("node-guests") gated by its own outer field ("node-group-trigger"),
    /// whose children exercise every row-scoping case: <c>child-b</c> shows while <c>child-a</c>
    /// (its row sibling) is "yes"; <c>child-c</c> shows while <c>child-b</c> is "yes" (a within-row
    /// a → b → c chain); <c>child-outer</c> shows while a *different* outer field
    /// ("node-child-trigger") is "yes" (a row → outer reference).
    /// </summary>
    private static FormDefinition RowScopedDefinition() => new()
    {
        Id = "form-row-scope",
        Name = "Row scope",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "Page one",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-1",
                        Title = "Section one",
                        Nodes =
                        [
                            new FormNode { Id = "node-group-trigger", Type = NodeType.YesNo, Label = "Group trigger" },
                            new FormNode { Id = "node-child-trigger", Type = NodeType.YesNo, Label = "Child trigger" },
                            new FormNode
                            {
                                Id = "node-guests",
                                Type = NodeType.Repeating,
                                Label = "Guests",
                                VisibleWhen = ShownWhen("node-group-trigger"),
                                Children =
                                [
                                    new FormNode { Id = "child-a", Type = NodeType.YesNo, Label = "A" },
                                    new FormNode { Id = "child-b", Type = NodeType.Text, Label = "B", VisibleWhen = ShownWhen("child-a") },
                                    new FormNode { Id = "child-c", Type = NodeType.Text, Label = "C", VisibleWhen = ShownWhen("child-b") },
                                    new FormNode { Id = "child-outer", Type = NodeType.Text, Label = "Outer", VisibleWhen = ShownWhen("node-child-trigger") },
                                ],
                            },
                        ],
                    },
                ],
            },
        ],
    };

    [Fact]
    public void WithinRowVisibilityChainsSettleInFilterToVisible()
    {
        var definition = RowScopedDefinition();
        var rows = RepeatingRows.Empty.AddRow();
        var rowId = rows.Rows[0].RowId;
        rows = rows
            .SetValue(rowId, "child-a", "no")
            .SetValue(rowId, "child-b", "yes") // stale: the chain requires child-a to be "yes"
            .SetValue(rowId, "child-c", "leaked")
            .SetValue(rowId, "child-outer", "shown");

        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-group-trigger"] = "yes",
            ["node-child-trigger"] = "no",
            ["node-guests"] = rows,
        };

        var filtered = VisibilityEvaluator.FilterToVisible(definition, values);
        var filteredRows = Assert.IsType<RepeatingRows>(filtered["node-guests"]);
        var filteredRow = Assert.Single(filteredRows.Rows);

        Assert.Equal(["child-a"], filteredRow.Values.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ARowToOuterReferenceResolvesInFilterToVisible()
    {
        var definition = RowScopedDefinition();
        var rows = RepeatingRows.Empty.AddRow();
        var rowId = rows.Rows[0].RowId;
        rows = rows.SetValue(rowId, "child-a", "no").SetValue(rowId, "child-outer", "shown");

        var whenOuterFieldIsYes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-group-trigger"] = "yes",
            ["node-child-trigger"] = "yes",
            ["node-guests"] = rows,
        };
        var whenOuterFieldIsNo = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-group-trigger"] = "yes",
            ["node-child-trigger"] = "no",
            ["node-guests"] = rows,
        };

        var filteredWhenYes = Assert.IsType<RepeatingRows>(
            VisibilityEvaluator.FilterToVisible(definition, whenOuterFieldIsYes)["node-guests"]);
        Assert.Contains("child-outer", filteredWhenYes.Rows[0].Values.Keys);

        var filteredWhenNo = Assert.IsType<RepeatingRows>(
            VisibilityEvaluator.FilterToVisible(definition, whenOuterFieldIsNo)["node-guests"]);
        Assert.DoesNotContain("child-outer", filteredWhenNo.Rows[0].Values.Keys);
    }

    [Fact]
    public void AHiddenGroupDropsAllRowsFromTheFilteredPayload()
    {
        var definition = RowScopedDefinition();
        var rows = RepeatingRows.Empty.AddRow().AddRow();
        rows = rows
            .SetValue(rows.Rows[0].RowId, "child-a", "yes")
            .SetValue(rows.Rows[1].RowId, "child-a", "yes");

        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-group-trigger"] = "no",
            ["node-guests"] = rows,
        };

        var filtered = VisibilityEvaluator.FilterToVisible(definition, values);

        Assert.False(filtered.ContainsKey("node-guests"));
    }

    [Fact]
    public void GetVisibleChildIdsResolvesAWithinRowChainAndAnOuterReference()
    {
        var definition = RowScopedDefinition();
        var group = RequireNode(definition, "node-guests");
        var row = new RepeatingRow
        {
            RowId = "row-1",
            Values = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["child-a"] = "yes",
                ["child-b"] = "yes",
            },
        };
        var outerValues = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-child-trigger"] = "yes",
        };

        var visibleChildIds = VisibilityEvaluator.GetVisibleChildIds(group, row, outerValues);

        Assert.Equal(["child-a", "child-b", "child-c", "child-outer"], visibleChildIds);
    }

    [Fact]
    public void GetVisibleChildIdsRejectsNullArguments()
    {
        var group = RequireNode(RowScopedDefinition(), "node-guests");
        var row = new RepeatingRow { RowId = "row-1" };
        var outerValues = new Dictionary<string, object?>(StringComparer.Ordinal);

        Assert.Throws<ArgumentNullException>(() => VisibilityEvaluator.GetVisibleChildIds(null!, row, outerValues));
        Assert.Throws<ArgumentNullException>(() => VisibilityEvaluator.GetVisibleChildIds(group, null!, outerValues));
        Assert.Throws<ArgumentNullException>(() => VisibilityEvaluator.GetVisibleChildIds(group, row, null!));
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
