using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Internal;
using BlazeForms.Resources;
using BlazeForms.Versioning;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="DesignerEditContext"/>: every mutation rebuilds the draft immutably, moves
/// <see cref="DesignerEditContext.Selection"/> to the node the mutation actually affected with the
/// right <see cref="DesignerFocusIntent"/>, raises exactly one localized announcement, and queues
/// the autosave; undo/redo round-trip through the capped-at-50 history restoring both the
/// definition and the selection each snapshot carries (PRD §4.1, §7, §11).
/// </summary>
/// <remarks>
/// Every test disposes its context via <c>await using</c> -- <see cref="DesignerEditContext"/>
/// owns an <see cref="AutosaveScheduler"/> that in turn owns a real
/// <see cref="CancellationTokenSource"/>, so leaving one undisposed at the end of a test is a
/// genuine (if harmless-at-process-exit) resource leak CA2000 is right to flag.
/// </remarks>
public sealed class DesignerEditContextTests
{
    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private static DesignerEditContext CreateContext(FormDefinition definition, IFormDefinitionStore? store = null) =>
        new(FormLifecycle.CreateDraft(definition), store ?? new InMemoryFormDefinitionStore());

    [Fact]
    public async Task AddNodeInsertsSelectsItWithNewNodeIntentAndAnnouncesTheSectionItLandedIn()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        DesignerAnnouncement? announcement = null;
        context.Announced += a => announcement = a;

        context.AddNode(NodeType.Email, "section-1");

        var nodes = context.Draft.Definition.Pages[0].Sections[0].Nodes;
        Assert.Equal(2, nodes.Count);
        var added = nodes[1];
        Assert.Equal(NodeType.Email, added.Type);

        Assert.Equal(added.Id, context.Selection.NodeId);
        Assert.Equal("section-1", context.Selection.SectionId);
        Assert.Equal("page-1", context.Selection.PageId);
        Assert.Equal(DesignerFocusIntent.NewNode, context.Selection.Intent);

        var expected = Localizer["AnnouncementNodeAdded", "Email", "Your details"].Value;
        Assert.Equal(expected, announcement!.Message);
        Assert.Equal(AriaLivePoliteness.Polite, announcement.Politeness);
    }

    [Fact]
    public async Task AddNodeThrowsWhenTheSectionDoesNotExist()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));

        Assert.Throws<ArgumentException>(() => context.AddNode(NodeType.Text, "no-such-section"));
    }

    // -- Group-scoped mutations (repeating-groups-plan.md, Increment C): AddChildNode, and every
    // other mutation method's own generalized behaviour once the node in question is a repeating
    // group's own child.

    [Fact]
    public async Task AddChildNodeInsertsIntoTheGroupsChildrenAndSelectsItScopedWithNewNodeIntent()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));
        DesignerAnnouncement? announcement = null;
        context.Announced += a => announcement = a;

        context.AddChildNode(NodeType.Select, "group-1");

        var group = context.Draft.Definition.FindNode("group-1")!;
        Assert.Equal(3, group.Children.Count);
        var added = group.Children[2];
        Assert.Equal(NodeType.Select, added.Type);

        Assert.Equal(added.Id, context.Selection.NodeId);
        Assert.Equal("group-1", context.Selection.GroupId);
        Assert.Equal("section-1", context.Selection.SectionId);
        Assert.Equal(DesignerFocusIntent.NewNode, context.Selection.Intent);

        var expected = Localizer["AnnouncementNodeAdded", "Dropdown", "Household members"].Value;
        Assert.Equal(expected, announcement!.Message);
    }

    [Fact]
    public async Task AddChildNodeThrowsWhenTheGroupDoesNotExist()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));

        Assert.Throws<ArgumentException>(() => context.AddChildNode(NodeType.Text, "no-such-group"));
    }

    [Fact]
    public async Task AddChildNodeThrowsForARepeatingType()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));

        Assert.Throws<ArgumentException>(() => context.AddChildNode(NodeType.Repeating, "group-1"));
    }

    [Fact]
    public async Task UpdateNodeReplacesAGroupsOwnChildAndKeepsTheSelectionScoped()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));
        var original = context.Draft.Definition.FindNode("child-a")!;

        context.UpdateNode(original with { Label = "Given name" });

        var updated = context.Draft.Definition.FindNode("child-a")!;
        Assert.Equal("Given name", updated.Label);
        Assert.Equal("child-a", context.Selection.NodeId);
        Assert.Equal("group-1", context.Selection.GroupId);
        Assert.Equal(DesignerFocusIntent.None, context.Selection.Intent);
    }

    [Fact]
    public async Task DeleteNodeOnAGroupsOwnChildSelectsTheNextSiblingStillScoped()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));

        context.DeleteNode("child-a");

        Assert.Null(context.Draft.Definition.FindNode("child-a"));
        Assert.Equal("child-b", context.Selection.NodeId);
        Assert.Equal("group-1", context.Selection.GroupId);
        Assert.Equal(DesignerFocusIntent.Neighbour, context.Selection.Intent);
    }

    /// <summary>
    /// Deleting a group's only remaining child selects the group's own scope, node-less, rather
    /// than falling all the way back out to the group's own top-level row -- the child-scoped
    /// counterpart to <see cref="DeleteNodeSelectsTheOwningSectionWhenItWasTheOnlyNode"/>.
    /// </summary>
    [Fact]
    public async Task DeleteNodeOnAGroupsOnlyChildSelectsTheGroupsOwnEmptyScope()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));

        context.DeleteNode("child-a");
        context.DeleteNode("child-b");

        Assert.Empty(context.Draft.Definition.FindNode("group-1")!.Children);
        Assert.Null(context.Selection.NodeId);
        Assert.Equal("group-1", context.Selection.GroupId);
        Assert.Equal("section-1", context.Selection.SectionId);
        Assert.Equal(DesignerFocusIntent.Neighbour, context.Selection.Intent);
    }

    [Fact]
    public async Task DuplicateNodeOnAGroupsOwnChildInsertsWithinThatSameGroupStillScoped()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));

        context.DuplicateNode("child-a");

        var children = context.Draft.Definition.FindNode("group-1")!.Children;
        Assert.Equal(3, children.Count);
        Assert.Equal(children[1].Id, context.Selection.NodeId);
        Assert.Equal("group-1", context.Selection.GroupId);
        Assert.Equal(DesignerFocusIntent.NewNode, context.Selection.Intent);
    }

    /// <summary>
    /// A move within a group announces the group's own label in place of a section's title --
    /// <c>AnnouncementNodeMoved</c>'s resx template is generic enough to reuse verbatim for
    /// either (repeating-groups-plan.md, Increment C's own "Moved to position {0} of {1} in
    /// '{2}'." example).
    /// </summary>
    [Fact]
    public async Task MoveNodeWithinSectionOnAGroupsOwnChildAnnouncesTheGroupsOwnLabel()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));
        DesignerAnnouncement? announcement = null;
        context.Announced += a => announcement = a;

        context.MoveNodeWithinSection("child-a", +1);

        var children = context.Draft.Definition.FindNode("group-1")!.Children;
        Assert.Equal("child-a", children[1].Id);
        Assert.Equal("child-a", context.Selection.NodeId);
        Assert.Equal("group-1", context.Selection.GroupId);
        Assert.Equal(DesignerFocusIntent.Moved, context.Selection.Intent);

        var expected = Localizer["AnnouncementNodeMoved", 2, 2, "Household members"].Value;
        Assert.Equal(expected, announcement!.Message);
    }

    [Fact]
    public async Task UpdateNodeReplacesContentKeepsTheIdAndDoesNotMoveFocus()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var original = context.Draft.Definition.Pages[0].Sections[0].Nodes[0];

        context.UpdateNode(original with { Label = "Given name" });

        var updated = context.Draft.Definition.Pages[0].Sections[0].Nodes[0];
        Assert.Equal("first-name", updated.Id);
        Assert.Equal("Given name", updated.Label);
        Assert.Equal(DesignerFocusIntent.None, context.Selection.Intent);
        Assert.Equal("first-name", context.Selection.NodeId);
    }

    [Fact]
    public async Task DeleteNodeSelectsTheNextSiblingWithNeighbourIntent()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));

        context.DeleteNode("node-a");

        Assert.DoesNotContain(context.Draft.Definition.Pages[0].Sections[0].Nodes, n => n.Id == "node-a");
        Assert.Equal("node-b", context.Selection.NodeId);
        Assert.Equal(DesignerFocusIntent.Neighbour, context.Selection.Intent);
    }

    [Fact]
    public async Task DeleteNodeSelectsThePreviousSiblingWhenDeletingTheLastNode()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));

        context.DeleteNode("node-c");

        Assert.Equal("node-b", context.Selection.NodeId);
        Assert.Equal(DesignerFocusIntent.Neighbour, context.Selection.Intent);
    }

    [Fact]
    public async Task DeleteNodeSelectsTheOwningSectionWhenItWasTheOnlyNode()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));

        context.DeleteNode("node-d"); // the only node in section-2

        Assert.Null(context.Selection.NodeId);
        Assert.Equal("section-2", context.Selection.SectionId);
        Assert.Equal(DesignerFocusIntent.Neighbour, context.Selection.Intent);
    }

    [Fact]
    public async Task DuplicateNodeInsertsACopyWithAFreshIdAndSelectsItWithNewNodeIntent()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));

        context.DuplicateNode("node-a");

        var nodes = context.Draft.Definition.Pages[0].Sections[0].Nodes;
        Assert.Equal(4, nodes.Count);
        var duplicate = nodes[1];
        Assert.NotEqual("node-a", duplicate.Id);
        Assert.Equal("Field A", duplicate.Label);
        Assert.Equal(duplicate.Id, context.Selection.NodeId);
        Assert.Equal(DesignerFocusIntent.NewNode, context.Selection.Intent);
    }

    [Fact]
    public async Task AddPageAppendsAndSelectsTheNewPage()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));

        context.AddPage();

        Assert.Equal(2, context.Draft.Definition.Pages.Count);
        var newPage = context.Draft.Definition.Pages[1];
        Assert.Equal(newPage.Id, context.Selection.PageId);
        Assert.Null(context.Selection.SectionId);
        Assert.Equal(DesignerFocusIntent.NewNode, context.Selection.Intent);
    }

    [Fact]
    public async Task AddSectionAppendsAndSelectsTheNewSectionAnnouncingThePageTitle()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        DesignerAnnouncement? announcement = null;
        context.Announced += a => announcement = a;

        context.AddSection("page-1");

        var newSection = context.Draft.Definition.Pages[0].Sections[1];
        Assert.Equal(newSection.Id, context.Selection.SectionId);
        Assert.Equal("page-1", context.Selection.PageId);
        Assert.Equal(DesignerFocusIntent.NewNode, context.Selection.Intent);
        Assert.Equal(Localizer["AnnouncementSectionAdded", "About you"].Value, announcement!.Message);
    }

    [Fact]
    public async Task RenamePageChangesTheTitleAndCanBeUndone()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1")); // page-1 starts as "About you"
        DesignerAnnouncement? announcement = null;
        context.Announced += a => announcement = a;

        context.RenamePage("page-1", "Contact details");

        Assert.Equal("Contact details", context.Draft.Definition.Pages[0].Title);
        Assert.Equal("page-1", context.Selection.PageId);
        Assert.Equal(DesignerFocusIntent.None, context.Selection.Intent);
        Assert.Equal(Localizer["AnnouncementPageRenamed", "Contact details"].Value, announcement!.Message);
        Assert.True(context.CanUndo);

        context.Undo();

        Assert.Equal("About you", context.Draft.Definition.Pages[0].Title);
    }

    [Fact]
    public async Task RenamePageToTheSameTitleIsANoOp()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1")); // page-1 starts as "About you"
        var definitionBefore = context.Draft.Definition;

        context.RenamePage("page-1", "About you");

        Assert.Same(definitionBefore, context.Draft.Definition);
        Assert.False(context.CanUndo);
    }

    [Fact]
    public async Task RenamePageToBlankClearsTheTitleToTheFallback()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1")); // page-1 starts as "About you"

        context.RenamePage("page-1", "   ");

        Assert.Null(context.Draft.Definition.Pages[0].Title);
    }

    [Fact]
    public async Task MoveNodeWithinSectionAnnouncesThePositionInPlainLanguage()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        DesignerAnnouncement? announcement = null;
        context.Announced += a => announcement = a;

        context.MoveNodeWithinSection("node-a", 1);

        Assert.Equal(1, context.Draft.Definition.Pages[0].Sections[0].Nodes.ToList().FindIndex(n => n.Id == "node-a"));
        Assert.Equal("node-a", context.Selection.NodeId);
        Assert.Equal(DesignerFocusIntent.Moved, context.Selection.Intent);
        Assert.Equal(Localizer["AnnouncementNodeMoved", 2, 3, "Transportation"].Value, announcement!.Message);
    }

    [Fact]
    public async Task MoveNodeWithinSectionAtTheBoundaryDoesNothingAndAnnouncesNothing()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var announced = false;
        var stateChanged = false;
        context.Announced += _ => announced = true;
        context.StateChanged += () => stateChanged = true;
        var draftBefore = context.Draft;

        context.MoveNodeWithinSection("node-a", -1); // already first

        Assert.Same(draftBefore, context.Draft);
        Assert.False(announced);
        Assert.False(stateChanged);
        Assert.False(context.CanUndo);
    }

    [Fact]
    public async Task MoveNodeAcrossSectionsMovesToTheTargetSectionAndSelectsItThere()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));

        context.MoveNodeAcrossSections("node-a", "section-2", 0);

        Assert.DoesNotContain(context.Draft.Definition.Pages[0].Sections[0].Nodes, n => n.Id == "node-a");
        Assert.Equal("node-a", context.Draft.Definition.Pages[0].Sections[1].Nodes[0].Id);
        Assert.Equal("section-2", context.Selection.SectionId);
        Assert.Equal(DesignerFocusIntent.Moved, context.Selection.Intent);
    }

    [Fact]
    public async Task MoveNodeToPositionTreatsThePositionAsOneBased()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));

        context.MoveNodeToPosition("node-c", "section-1", 1); // "position 1" == index 0

        Assert.Equal("node-c", context.Draft.Definition.Pages[0].Sections[0].Nodes[0].Id);
    }

    [Fact]
    public async Task SetValidationRulesReplacesTheRuleSetAndAnnounces()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        DesignerAnnouncement? announcement = null;
        context.Announced += a => announcement = a;
        IReadOnlyList<Expressions.ValidationRule> rules =
        [
            new Expressions.ValidationRule
            {
                Target = "first-name",
                Message = "Enter a first name.",
                Expression = new Expressions.ConditionGroup(),
            },
        ];

        context.SetValidationRules(rules);

        Assert.Single(context.Draft.Definition.ValidationRules);
        Assert.Equal(Localizer["AnnouncementValidationRulesUpdated"].Value, announcement!.Message);
    }

    [Fact]
    public async Task SelectMovesTheSelectionWithoutTouchingTheDraftTheUndoStackOrAnnouncing()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var definitionBefore = context.Draft.Definition;
        var announced = false;
        var stateChanged = false;
        context.Announced += _ => announced = true;
        context.StateChanged += () => stateChanged = true;

        context.Select(DesignerSelection.ForNode("node-b", "page-1", "section-1", DesignerFocusIntent.None));

        Assert.Equal("node-b", context.Selection.NodeId);
        Assert.Same(definitionBefore, context.Draft.Definition);
        Assert.False(context.IsDirty);
        Assert.False(context.CanUndo);
        Assert.False(announced);
        Assert.True(stateChanged);
    }

    [Fact]
    public async Task ExactlyOneAnnouncementAndOneStateChangedFirePerMutation()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var announcementCount = 0;
        var stateChangedCount = 0;
        context.Announced += _ => announcementCount++;
        context.StateChanged += () => stateChangedCount++;

        context.AddNode(NodeType.Text, "section-1");

        Assert.Equal(1, announcementCount);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public async Task EachMutationMarksTheContextDirty()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));

        Assert.False(context.IsDirty);
        context.AddNode(NodeType.Text, "section-1");
        Assert.True(context.IsDirty);
    }

    [Fact]
    public async Task TheFirstMutationTriggersAnImmediateAutosave()
    {
        var store = new SaveTrackingFormDefinitionStore();
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"), store);

        context.AddNode(NodeType.Text, "section-1");
        await context.PendingAutosave.ConfigureAwait(true);

        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task AnAutosaveFailureIsSurfacedViaAutosaveFailedAndLeavesTheContextUsable()
    {
        var store = new ThrowingFormDefinitionStore(failuresBeforeSucceeding: 1);
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"), store);
        Exception? observed = null;
        context.AutosaveFailed += ex => observed = ex;

        context.AddNode(NodeType.Text, "section-1"); // the first mutation's autosave is immediate -- and fails
        await context.PendingAutosave.ConfigureAwait(true);

        Assert.IsType<InvalidOperationException>(observed);

        // The context stays usable: a later mutation's own autosave still succeeds.
        context.AddNode(NodeType.Email, "section-1");
        await context.PendingAutosave.ConfigureAwait(true);
        Assert.Equal(1, store.SuccessfulSaveCount);
    }

    [Fact]
    public async Task DisposeAsyncDoesNotThrowWhenAPendingAutosaveHadFailed()
    {
        var store = new ThrowingFormDefinitionStore(failuresBeforeSucceeding: int.MaxValue);
        var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"), store);

        context.AddNode(NodeType.Text, "section-1");
        await context.PendingAutosave.ConfigureAwait(true); // let the always-failing save actually run and fail

        await context.DisposeAsync(); // must not rethrow the failure that already happened
    }

    [Fact]
    public async Task UndoRestoresThePriorDefinitionAndSelectionAndAnnouncesWhatWasUndone()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        context.AddNode(NodeType.Text, "section-1");
        var afterAdd = context.Draft;
        DesignerAnnouncement? announcement = null;
        context.Announced += a => announcement = a;

        context.Undo();

        Assert.NotSame(afterAdd, context.Draft);
        Assert.Single(context.Draft.Definition.Pages[0].Sections[0].Nodes); // back to the original one field
        Assert.Equal(DesignerFocusIntent.Restored, context.Selection.Intent);
        Assert.False(context.CanUndo);
        Assert.True(context.CanRedo);
        Assert.StartsWith("Undid:", announcement!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedoReappliesTheUndoneMutationAndAnnouncesWhatWasRedone()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        context.AddNode(NodeType.Text, "section-1");
        var afterAdd = context.Draft;
        context.Undo();
        DesignerAnnouncement? announcement = null;
        context.Announced += a => announcement = a;

        context.Redo();

        Assert.Equal(afterAdd.Definition, context.Draft.Definition);
        Assert.True(context.CanUndo);
        Assert.False(context.CanRedo);
        Assert.StartsWith("Redid:", announcement!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UndoWithNothingToUndoIsANoOp()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var announced = false;
        context.Announced += _ => announced = true;

        context.Undo();

        Assert.False(announced);
        Assert.False(context.CanUndo);
    }

    [Fact]
    public async Task RedoWithNothingToRedoIsANoOp()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var announced = false;
        context.Announced += _ => announced = true;

        context.Redo();

        Assert.False(announced);
        Assert.False(context.CanRedo);
    }

    [Fact]
    public async Task ANewMutationAfterAnUndoClearsTheRedoStack()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        context.AddNode(NodeType.Text, "section-1");
        context.Undo();
        Assert.True(context.CanRedo);

        context.AddNode(NodeType.Email, "section-1");

        Assert.False(context.CanRedo);
    }

    [Fact]
    public async Task UndoingEveryMutationRestoresTheOriginalDefinitionAndRedoingAllRestoresTheFinalOne()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var original = context.Draft.Definition;

        context.AddNode(NodeType.Text, "section-1");
        context.DeleteNode("node-b");
        context.MoveNodeWithinSection("node-a", 1);
        var final = context.Draft.Definition;

        context.Undo();
        context.Undo();
        context.Undo();

        Assert.Equal(original, context.Draft.Definition);
        // Undo restores the exact prior FormDefinition instance each EditSnapshot captured, not
        // merely a structurally equal rebuild -- pinning this so a future refactor of Restore
        // (e.g. reaching for DefinitionMutations instead of the snapshot itself) can't silently
        // regress the "instance, not equal-value" half of AGENTS.md invariant #3.
        Assert.Same(original, context.Draft.Definition);
        Assert.False(context.CanUndo);

        context.Redo();
        context.Redo();
        context.Redo();

        Assert.Equal(final, context.Draft.Definition);
        Assert.False(context.CanRedo);
    }

    [Fact]
    public async Task FiftyOneMutationsKeepExactlyFiftyUndoStepsDroppingTheOldest()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        FormDefinition? afterFirstMutation = null;

        for (var i = 0; i < 51; i++)
        {
            context.AddNode(NodeType.Text, "section-1", index: 0);

            if (i == 0)
            {
                afterFirstMutation = context.Draft.Definition;
            }
        }

        for (var i = 0; i < 50; i++)
        {
            Assert.True(context.CanUndo);
            context.Undo();
        }

        // The 51st mutation's undo stack has room for only 50 prior states, so the very first
        // mutation's own "before" snapshot (the original one-field definition) was dropped; the
        // oldest state still reachable is the definition immediately after that first mutation.
        Assert.False(context.CanUndo);
        Assert.Equal(afterFirstMutation, context.Draft.Definition);
    }
}
