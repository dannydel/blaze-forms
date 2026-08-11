using BlazeForms.Definitions;
using BlazeForms.Designer.Internal;
using BlazeForms.Expressions;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="DefinitionMutations"/> in isolation: every rebuild returns a new
/// <see cref="FormDefinition"/> while leaving the one passed in byte-for-byte unchanged
/// (AGENTS.md invariant #3), stable identifiers survive a duplicate untouched (invariant #5), and
/// every move clamps rather than throwing on an out-of-range index.
/// </summary>
public sealed class DefinitionMutationsTests
{
    [Fact]
    public void InsertNodeAddsWithoutMutatingTheOriginalDefinition()
    {
        var original = DesignerTestFixtures.OneFieldDefinition("form-1");
        var node = new FormNode { Id = "node-new", Type = NodeType.Text };

        var updated = DefinitionMutations.InsertNode(original, "section-1", node, index: null);

        Assert.Single(original.Pages[0].Sections[0].Nodes);
        Assert.Equal(2, updated.Pages[0].Sections[0].Nodes.Count);
        Assert.Equal("node-new", updated.Pages[0].Sections[0].Nodes[1].Id);
    }

    [Fact]
    public void InsertNodeClampsAnOutOfRangeIndex()
    {
        var original = DesignerTestFixtures.OneFieldDefinition("form-1");
        var node = new FormNode { Id = "node-new", Type = NodeType.Text };

        var updated = DefinitionMutations.InsertNode(original, "section-1", node, index: 99);

        Assert.Equal(1, updated.Pages[0].Sections[0].Nodes.ToList().IndexOf(node));
    }

    [Fact]
    public void InsertNodeThrowsWhenTheSectionDoesNotExist()
    {
        var original = DesignerTestFixtures.OneFieldDefinition("form-1");
        var node = new FormNode { Id = "node-new", Type = NodeType.Text };

        Assert.Throws<ArgumentException>(() => DefinitionMutations.InsertNode(original, "no-such-section", node, null));
    }

    [Fact]
    public void RemoveNodeRemovesWithoutMutatingTheOriginalDefinition()
    {
        var original = DesignerTestFixtures.TwoSectionDefinition("form-1");

        var updated = DefinitionMutations.RemoveNode(original, "node-b");

        Assert.Equal(3, original.Pages[0].Sections[0].Nodes.Count);
        Assert.Equal(2, updated.Pages[0].Sections[0].Nodes.Count);
        Assert.DoesNotContain(updated.Pages[0].Sections[0].Nodes, n => n.Id == "node-b");
    }

    [Fact]
    public void UpdateNodeReplacesInPlaceWithoutMutatingTheOriginalDefinition()
    {
        var original = DesignerTestFixtures.OneFieldDefinition("form-1");
        var replacement = original.Pages[0].Sections[0].Nodes[0] with { Label = "Given name" };

        var updated = DefinitionMutations.UpdateNode(original, replacement);

        Assert.Equal("First name", original.Pages[0].Sections[0].Nodes[0].Label);
        Assert.Equal("Given name", updated.Pages[0].Sections[0].Nodes[0].Label);
        Assert.Equal("first-name", updated.Pages[0].Sections[0].Nodes[0].Id);
    }

    [Fact]
    public void DuplicateNodeInsertsImmediatelyAfterTheOriginalWithAFreshId()
    {
        var original = DesignerTestFixtures.TwoSectionDefinition("form-1");

        var (updated, duplicate) = DefinitionMutations.DuplicateNode(original, "node-a");

        Assert.Equal(3, original.Pages[0].Sections[0].Nodes.Count);
        var nodes = updated.Pages[0].Sections[0].Nodes;
        Assert.Equal(4, nodes.Count);
        Assert.Equal("node-a", nodes[0].Id);
        Assert.Equal(duplicate.Id, nodes[1].Id);
        Assert.NotEqual("node-a", duplicate.Id);
        Assert.Equal("Field A", duplicate.Label);
    }

    [Fact]
    public void DuplicateNodeRecursivelyReIdsEveryDescendantAndPreservesOptionValues()
    {
        var grandchild = new FormNode { Id = "grandchild-1", Type = NodeType.Text, Label = "Grandchild" };
        var child = new FormNode
        {
            Id = "child-1",
            Type = NodeType.Select,
            Label = "Child field",
            Options = [new FormOption { Value = "opt-a", Label = "Option A" }],
            Children = [grandchild],
        };
        var parent = new FormNode { Id = "node-parent", Type = NodeType.Repeating, Children = [child] };
        var original = new FormDefinition
        {
            Id = "form-1",
            Name = "Repeating group form",
            Pages =
            [
                new FormPage
                {
                    Id = "page-1",
                    Sections = [new FormSection { Id = "section-1", Nodes = [parent] }],
                },
            ],
        };

        var (_, duplicate) = DefinitionMutations.DuplicateNode(original, "node-parent");

        Assert.NotEqual("node-parent", duplicate.Id);
        var duplicateChild = duplicate.Children[0];
        Assert.NotEqual("child-1", duplicateChild.Id);
        Assert.Equal("Child field", duplicateChild.Label);
        Assert.Equal("opt-a", duplicateChild.Options[0].Value);
        Assert.Equal("Option A", duplicateChild.Options[0].Label);
        var duplicateGrandchild = duplicateChild.Children[0];
        Assert.NotEqual("grandchild-1", duplicateGrandchild.Id);
        Assert.Equal("Grandchild", duplicateGrandchild.Label);

        // Every id in the duplicated subtree is fresh and disjoint from the original's, all the
        // way down.
        string[] originalIds = ["node-parent", "child-1", "grandchild-1"];
        string[] duplicateIds = [duplicate.Id, duplicateChild.Id, duplicateGrandchild.Id];
        Assert.Empty(originalIds.Intersect(duplicateIds, StringComparer.Ordinal));
        Assert.Equal(3, duplicateIds.Distinct(StringComparer.Ordinal).Count());

        // The original subtree is untouched.
        Assert.Equal("child-1", parent.Children[0].Id);
        Assert.Equal("grandchild-1", parent.Children[0].Children[0].Id);
    }

    [Fact]
    public void DuplicateNodeCopiesOptionValuesVerbatim()
    {
        var original = DesignerTestFixtures.OptionNodeDefinition("form-1");

        var (_, duplicate) = DefinitionMutations.DuplicateNode(original, "node-choice");

        Assert.Equal(2, duplicate.Options.Count);
        Assert.Equal("opt-1", duplicate.Options[0].Value);
        Assert.Equal("opt-2", duplicate.Options[1].Value);
        Assert.Equal("Option one", duplicate.Options[0].Label);
    }

    [Fact]
    public void AddPageAppendsAnEmptyPageWithoutMutatingTheOriginalDefinition()
    {
        var original = DesignerTestFixtures.OneFieldDefinition("form-1");
        var page = new FormPage { Id = "page-new" };

        var updated = DefinitionMutations.AddPage(original, page);

        Assert.Single(original.Pages);
        Assert.Equal(2, updated.Pages.Count);
        Assert.Empty(updated.Pages[1].Sections);
    }

    [Fact]
    public void AddSectionAppendsAnEmptySectionToTheNamedPage()
    {
        var original = DesignerTestFixtures.OneFieldDefinition("form-1");
        var section = new FormSection { Id = "section-new" };

        var updated = DefinitionMutations.AddSection(original, "page-1", section);

        Assert.Single(original.Pages[0].Sections);
        Assert.Equal(2, updated.Pages[0].Sections.Count);
        Assert.Empty(updated.Pages[0].Sections[1].Nodes);
    }

    [Fact]
    public void AddSectionThrowsWhenThePageDoesNotExist()
    {
        var original = DesignerTestFixtures.OneFieldDefinition("form-1");

        Assert.Throws<ArgumentException>(() =>
            DefinitionMutations.AddSection(original, "no-such-page", new FormSection { Id = "section-new" }));
    }

    [Theory]
    [InlineData(1, 2)] // b moves later, past c -> becomes last (index 2)
    [InlineData(-1, 0)] // b moves earlier -> becomes first (index 0)
    [InlineData(-99, 0)] // clamps at the start
    [InlineData(99, 2)] // clamps at the end
    public void MoveNodeWithinSectionMovesAndClamps(int delta, int expectedIndex)
    {
        var original = DesignerTestFixtures.TwoSectionDefinition("form-1");

        var updated = DefinitionMutations.MoveNodeWithinSection(original, "node-b", delta);

        Assert.Equal(expectedIndex, updated.Pages[0].Sections[0].Nodes.ToList().FindIndex(n => n.Id == "node-b"));
        // The original section's order is untouched.
        Assert.Equal("node-b", original.Pages[0].Sections[0].Nodes[1].Id);
    }

    [Fact]
    public void MoveNodeWithinSectionIsANoOpAtTheStartMovingEarlier()
    {
        var original = DesignerTestFixtures.TwoSectionDefinition("form-1");

        var updated = DefinitionMutations.MoveNodeWithinSection(original, "node-a", -1);

        Assert.Same(original, updated);
    }

    [Fact]
    public void MoveNodeWithinSectionIsANoOpAtTheEndMovingLater()
    {
        var original = DesignerTestFixtures.TwoSectionDefinition("form-1");

        var updated = DefinitionMutations.MoveNodeWithinSection(original, "node-c", 1);

        Assert.Same(original, updated);
    }

    [Fact]
    public void MoveNodeAcrossSectionsRemovesFromSourceAndInsertsAtTheClampedTargetIndex()
    {
        var original = DesignerTestFixtures.TwoSectionDefinition("form-1");

        var updated = DefinitionMutations.MoveNode(original, "node-a", "section-2", 99);

        Assert.Equal(3, original.Pages[0].Sections[0].Nodes.Count);
        Assert.DoesNotContain(updated.Pages[0].Sections[0].Nodes, n => n.Id == "node-a");
        Assert.Equal(2, updated.Pages[0].Sections[1].Nodes.Count);
        Assert.Equal("node-a", updated.Pages[0].Sections[1].Nodes[^1].Id);
    }

    [Fact]
    public void MoveNodeWithinTheSameSectionAtItsCurrentClampedPositionIsANoOp()
    {
        var original = DesignerTestFixtures.TwoSectionDefinition("form-1");

        // node-b is already at index 1 of a 3-node section; asking to move it there again
        // (post-removal length 2, clamped 1 -> 1) must not manufacture a change.
        var updated = DefinitionMutations.MoveNode(original, "node-b", "section-1", 1);

        Assert.Same(original, updated);
    }

    [Fact]
    public void SetValidationRulesReplacesTheRuleSetWithoutMutatingTheOriginalDefinition()
    {
        var original = DesignerTestFixtures.OneFieldDefinition("form-1");
        IReadOnlyList<ValidationRule> rules =
        [
            new ValidationRule
            {
                Target = "first-name",
                Message = "Enter a first name.",
                Expression = new ConditionGroup { Join = ConditionJoin.All, Conditions = [] },
            },
        ];

        var updated = DefinitionMutations.SetValidationRules(original, rules);

        Assert.Empty(original.ValidationRules);
        Assert.Single(updated.ValidationRules);
    }

    [Fact]
    public void FindNodeLocationReturnsNullWhenTheNodeIsNotInTheDefinition()
    {
        var definition = DesignerTestFixtures.OneFieldDefinition("form-1");

        Assert.Null(DefinitionMutations.FindNodeLocation(definition, "no-such-node"));
    }

    [Fact]
    public void FindPageIndexReturnsNullWhenThePageIsNotInTheDefinition()
    {
        var definition = DesignerTestFixtures.OneFieldDefinition("form-1");

        Assert.Null(DefinitionMutations.FindPageIndex(definition, "no-such-page"));
    }
}
