using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Linting;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="LinterDock"/> (Phase 7, PRD §8): it lints the working draft debounced
/// through <see cref="Internal.LintScheduler"/>, lists every <see cref="LintResult"/> with a
/// text-conveyed severity, collapses/expands via <c>aria-expanded</c>, jumps to a finding's node
/// as pure navigation (never an undoable mutation), one-click-fixes <see cref="LintRuleIds.A11y08"/>,
/// and coalesces a burst of rapid mutations into a single lint pass.
/// </summary>
public sealed class LinterDockTests : DesignerTestContext
{
    private static DesignerEditContext CreateContext(FormDefinition definition, IFormDefinitionStore? store = null) =>
        new(FormLifecycle.CreateDraft(definition), store ?? new InMemoryFormDefinitionStore());

    [Fact]
    public async Task ListsResultsFromASeededDefinitionWithAKnownBlockingLint()
    {
        await using var context = CreateContext(DesignerTestFixtures.UntitledNodeDefinition("form-1"));
        var cut = Render<LinterDock>(p => p.Add(d => d.EditContext, context));

        await cut.Instance.PendingLint.ConfigureAwait(true);
        cut.Render();

        Assert.Contains(cut.Instance.Results, r => r.RuleId == LintRuleIds.A11y01);
        Assert.Contains("A11Y-01", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Blocking", cut.Markup, StringComparison.Ordinal);
        Assert.True(cut.Find("button.bf-linter-dock__toggle").GetAttribute("aria-expanded") == "true");
    }

    [Fact]
    public async Task CollapsingHidesTheFindingsListAndFlipsAriaExpanded()
    {
        await using var context = CreateContext(DesignerTestFixtures.UntitledNodeDefinition("form-1"));
        var cut = Render<LinterDock>(p => p.Add(d => d.EditContext, context));
        await cut.Instance.PendingLint.ConfigureAwait(true);
        cut.Render();
        Assert.NotEmpty(cut.FindAll("li.bf-linter-dock__item"));

        await cut.Find("button.bf-linter-dock__toggle").ClickAsync(new MouseEventArgs());

        Assert.Equal("false", cut.Find("button.bf-linter-dock__toggle").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("li.bf-linter-dock__item"));
    }

    [Fact]
    public async Task JumpToNodeSelectsThePageSectionAndNodeAndRequestsFocus()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoPageBlockingIssueDefinition("form-1"));
        var cut = Render<LinterDock>(p => p.Add(d => d.EditContext, context));
        await cut.Instance.PendingLint.ConfigureAwait(true);
        cut.Render();

        await cut.Find("button.bf-linter-dock__jump").ClickAsync(new MouseEventArgs());

        Assert.Equal("page-2", context.Selection.PageId);
        Assert.Equal("section-2", context.Selection.SectionId);
        Assert.Equal("node-b", context.Selection.NodeId);
        Assert.Equal(DesignerFocusIntent.JumpedTo, context.Selection.Intent);
    }

    [Fact]
    public async Task JumpToNodeNeverPushesAnUndoEntry()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoPageBlockingIssueDefinition("form-1"));
        var cut = Render<LinterDock>(p => p.Add(d => d.EditContext, context));
        await cut.Instance.PendingLint.ConfigureAwait(true);
        cut.Render();

        await cut.Find("button.bf-linter-dock__jump").ClickAsync(new MouseEventArgs());

        // Navigation only -- PRD §4.1: jump-to-node is not an undoable edit.
        Assert.False(context.CanUndo);
    }

    [Fact]
    public async Task NoJumpButtonWhenTheFindingHasNoLiveNode()
    {
        // FR-03's own anchor is null once the referenced field itself no longer exists (Core's
        // DanglingReferenceRule) -- jump-to-node has nothing to jump to, so it must not render.
        await using var context = CreateContext(DesignerTestFixtures.ReferencedFieldDefinition("form-1"));
        context.DeleteNode("node-referenced");
        var cut = Render<LinterDock>(p => p.Add(d => d.EditContext, context));
        await cut.Instance.PendingLint.ConfigureAwait(true);
        cut.Render();

        // Three FR-03 lines name the deleted field: node-dependent's own dangling visibility rule
        // (still anchored to node-dependent, a live node -- jump-to-node works there) and the
        // validation rule's own dangling target and expression (anchored to nothing at all, since
        // the field the rule pointed at is exactly the one just deleted -- no jump-to-node for
        // either of those two).
        var danglingItems = cut.FindAll("li.bf-linter-dock__item")
            .Where(li => li.QuerySelector("span.bf-linter-dock__rule-id")!.TextContent == LintRuleIds.Fr03
                && li.TextContent.Contains("node-referenced", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, danglingItems.Count);
        Assert.Equal(2, danglingItems.Count(item => item.QuerySelector("button.bf-linter-dock__jump") is null));
    }

    [Fact]
    public async Task OneClickFixForHeadingSkipMutatesTheLevelAndTheFindingClearsOnReLint()
    {
        await using var context = CreateContext(DesignerTestFixtures.HeadingSkipDefinition("form-1"));
        var cut = Render<LinterDock>(p => p.Add(d => d.EditContext, context));
        await cut.Instance.PendingLint.ConfigureAwait(true);
        cut.Render();
        Assert.Contains(cut.Instance.Results, r => r.RuleId == LintRuleIds.A11y08);

        await cut.Find("button.bf-linter-dock__fix").ClickAsync(new MouseEventArgs());
        await cut.Instance.PendingLint.ConfigureAwait(true);
        cut.Render();

        Assert.Equal(3, context.Draft.Definition.FindNode("node-h2")!.Level);
        Assert.DoesNotContain(cut.Instance.Results, r => r.RuleId == LintRuleIds.A11y08);
    }

    [Fact]
    public async Task OnlyHeadingSkipGetsAFixButtonEveryOtherRuleIsJumpOnly()
    {
        await using var context = CreateContext(DesignerTestFixtures.UntitledNodeDefinition("form-1"));
        var cut = Render<LinterDock>(p => p.Add(d => d.EditContext, context));
        await cut.Instance.PendingLint.ConfigureAwait(true);
        cut.Render();

        Assert.Contains(cut.Instance.Results, r => r.RuleId == LintRuleIds.A11y01);
        Assert.Empty(cut.FindAll("button.bf-linter-dock__fix"));
    }

    [Fact]
    public async Task RapidMutationsCoalesceIntoOneLintPassPastTheInitialOne()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<LinterDock>(p => p.Add(d => d.EditContext, context));
        await cut.Instance.PendingLint.ConfigureAwait(true);
        var runCountAfterInitialLoad = cut.Instance.LintRunCount;

        for (var i = 0; i < 5; i++)
        {
            context.UpdateNode(context.Draft.Definition.FindNode("node-a")! with { Label = $"Field A v{i}" });
        }

        await cut.Instance.PendingLint.ConfigureAwait(true);

        // Five rapid mutations debounce into exactly one further lint pass, not five.
        Assert.Equal(runCountAfterInitialLoad + 1, cut.Instance.LintRunCount);
    }

    [Fact]
    public async Task EmptyDefinitionShowsTheEmptyText()
    {
        await using var context = CreateContext(new FormDefinition { Id = "form-1", Name = "Blank" });
        var cut = Render<LinterDock>(p => p.Add(d => d.EditContext, context));

        await cut.Instance.PendingLint.ConfigureAwait(true);
        cut.Render();

        Assert.Contains("No issues found", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheInitialLintPassOnMountNeverAnnouncesEvenWithASeededBlockingFinding()
    {
        // UntitledNodeDefinition's own node is already blocking A11Y-01 the moment the designer
        // opens onto it -- the author has not acted on anything yet, so the very first completed
        // pass must establish that baseline silently rather than assertively interrupting them
        // with a finding that predates any edit of theirs (code review fix, PRD §11).
        await using var context = CreateContext(DesignerTestFixtures.UntitledNodeDefinition("form-1"));
        string? announcedMessage = null;
        context.Announced += announcement => announcedMessage = announcement.Message;

        var cut = Render<LinterDock>(p => p.Add(d => d.EditContext, context));
        await cut.Instance.PendingLint.ConfigureAwait(true);
        cut.Render();

        Assert.Contains(cut.Instance.Results, r => r.RuleId == LintRuleIds.A11y01);
        Assert.Null(announcedMessage);
    }

    [Fact]
    public async Task AMutationThatChangesTheLintPictureAfterTheBaselinePassStillAnnounces()
    {
        await using var context = CreateContext(DesignerTestFixtures.UntitledNodeDefinition("form-1"));
        var cut = Render<LinterDock>(p => p.Add(d => d.EditContext, context));
        await cut.Instance.PendingLint.ConfigureAwait(true);

        string? announcedMessage = null;
        context.Announced += announcement => announcedMessage = announcement.Message;

        // Labelling the previously-untitled node clears the blocking A11Y-01 finding -- a real
        // transition the baseline pass above never saw, so it does announce.
        context.UpdateNode(context.Draft.Definition.FindNode("node-untitled")! with { Label = "Email address" });
        await cut.Instance.PendingLint.ConfigureAwait(true);

        Assert.NotNull(announcedMessage);
        Assert.DoesNotContain(cut.Instance.Results, r => r.RuleId == LintRuleIds.A11y01);
    }

    [Fact]
    public async Task ALintPassWithUnchangedResultsSkipsTheRedundantRerenderButStillBridgesToTheCanvas()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var resultsChangedCallCount = 0;
        var cut = Render<LinterDock>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.ResultsChanged, (IReadOnlyList<LintResult> _) => resultsChangedCallCount++));
        await cut.Instance.PendingLint.ConfigureAwait(true);
        cut.Render();
        var renderCountAfterInitialLoad = cut.RenderCount;
        var runCountAfterInitialLoad = cut.Instance.LintRunCount;

        // A label change with no lint impact at all -- TwoSectionDefinition's fields are already
        // fully labelled -- so the fresh (but value-equal) LintResult list this pass produces
        // must not trigger a re-render (code review fix/nit), even though the debounced pass
        // itself still ran and still reached ResultsChanged.
        context.UpdateNode(context.Draft.Definition.FindNode("node-a")! with { Label = "Renamed Field A" });
        await cut.Instance.PendingLint.ConfigureAwait(true);

        Assert.Equal(runCountAfterInitialLoad + 1, cut.Instance.LintRunCount);
        Assert.Equal(renderCountAfterInitialLoad, cut.RenderCount);
        Assert.True(resultsChangedCallCount >= 2);
    }

    [Fact]
    public async Task DisposeAsyncCancelsAPendingLintWithoutThrowing()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<LinterDock>(p => p.Add(d => d.EditContext, context));
        await cut.Instance.PendingLint.ConfigureAwait(true);

        context.UpdateNode(context.Draft.Definition.FindNode("node-a")! with { Label = "Changed" });

        await cut.Instance.DisposeAsync();
    }
}
