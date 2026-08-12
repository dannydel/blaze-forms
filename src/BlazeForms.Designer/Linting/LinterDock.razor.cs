using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Designer;
using BlazeForms.Designer.Internal;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Linting;

/// <summary>
/// The collapsible linter dock (PRD §8): re-lints the working draft on every
/// <see cref="DesignerEditContext.StateChanged"/>, debounced through <see cref="LintScheduler"/>
/// so a burst of rapid mutations coalesces into one pass, and lists every
/// <see cref="LintResult"/> with its message, detail, rule ID, a severity conveyed by text (not
/// color alone), a jump-to-node action when the result anchors to a live node, and a one-click
/// fix for the one rule this phase can deterministically repair (<see cref="LintRuleIds.A11y08"/>
/// -- a heading that skips a rung).
/// </summary>
/// <remarks>
/// <para>
/// <b>Jump-to-node is navigation, not an edit.</b> <see cref="JumpToNode"/> calls
/// <see cref="DesignerEditContext.Select"/> directly -- the same method the canvas's own
/// click-to-select path uses -- rather than any undoable mutation: PRD §4.1 draws that line
/// explicitly ("jump-to-node changes active page + selection but is not an undoable edit"). The
/// new <see cref="DesignerFocusIntent.JumpedTo"/> selection carries a real page/section/node
/// triple, so <c>FormDesigner</c>'s own <c>SyncActivePageFromSelection</c> (already wired to
/// <see cref="DesignerEditContext.StateChanged"/>) switches the active page, and
/// <c>DesignerCanvas</c>'s own selection-follow logic moves the roving cursor and requests real
/// DOM focus for the target row -- this dock needs no callback parameter of its own to reach
/// either.
/// </para>
/// <para>
/// <b>Announcements.</b> A mutation's own plain-language description is announced by
/// <c>DesignerEditContext</c>'s own <c>Commit</c> already; this dock only speaks when the lint picture
/// itself actually changes, through the same shared <see cref="AriaLiveRegion"/> via
/// <see cref="DesignerEditContext.Announce"/> -- a change in how many results are blocking speaks
/// assertively (an author needs to know before they try to publish), a growing total result count
/// with the same blocking count speaks politely, and a lint pass that finds nothing new (the
/// common case -- most mutations do not change the lint picture at all) stays silent rather than
/// re-announcing the same standing findings on every debounce tick. The very first completed pass
/// is silent unconditionally, even when the draft it opened onto already carries a standing
/// blocking finding -- that first pass only ever establishes the baseline an author has not yet
/// acted on, never a transition worth interrupting them for.
/// </para>
/// </remarks>
public partial class LinterDock : ComponentBase, IAsyncDisposable
{
    private readonly string _instanceId = "bf-linter-dock-" + Guid.NewGuid().ToString("n");
    private LintScheduler? _scheduler;
    private DesignerEditContext? _subscribedContext;
    private IReadOnlyList<LintResult> _results = [];
    private bool _isExpanded = true;
    private int _lintRunCount;
    private bool _disposed;

    /// <summary>
    /// The mutation engine this dock lints the working draft of.
    /// </summary>
    [Parameter, EditorRequired]
    public DesignerEditContext EditContext { get; set; } = default!;

    /// <summary>
    /// Raised with every lint pass's own results, whether or not this dock is currently
    /// expanded -- <c>FormDesigner</c>'s own hook for handing the same pass's results down to
    /// <c>DesignerCanvas</c> as <see cref="BlazeForms.Canvas.DesignerCanvas.LintResults"/>, so a
    /// node's own inline findings come from this dock's exact lint pass rather than a second one.
    /// </summary>
    [Parameter]
    public EventCallback<IReadOnlyList<LintResult>> ResultsChanged { get; set; }

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private string BodyId => _instanceId + "-body";

    /// <summary>
    /// The most recent lint pass's results. <c>internal</c>, not <c>private</c>, purely so a test
    /// can assert the dock's standing findings without parsing rendered markup.
    /// </summary>
    internal IReadOnlyList<LintResult> Results => _results;

    /// <summary>
    /// How many lint passes have actually completed -- counted whether or not that pass's own
    /// results actually differed from <see cref="Results"/>, since a debounce coalescing rapid
    /// mutations into fewer passes is exactly what this counts. <c>internal</c> purely so a test
    /// can prove a burst of rapid mutations coalesces into far fewer runs than mutations, rather
    /// than one run per mutation.
    /// </summary>
    internal int LintRunCount => _lintRunCount;

    /// <inheritdoc/>
    protected override void OnInitialized() => _scheduler = new LintScheduler(OnLinted);

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_subscribedContext, EditContext))
        {
            return;
        }

        if (_subscribedContext is not null)
        {
            _subscribedContext.StateChanged -= OnEditContextStateChanged;
        }

        EditContext.StateChanged += OnEditContextStateChanged;
        _subscribedContext = EditContext;
        _scheduler!.ScheduleLint(EditContext.Draft.Definition);
    }

    /// <summary>
    /// Unsubscribes from <see cref="EditContext"/> and disposes the debounce scheduler, cancelling
    /// any pending lint pass. Safe to call more than once.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Blazor disposes a component on its own renderer's synchronization context, same as every other lifecycle method in this file.")]
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_subscribedContext is not null)
        {
            _subscribedContext.StateChanged -= OnEditContextStateChanged;
            _subscribedContext = null;
        }

        if (_scheduler is not null)
        {
            await _scheduler.DisposeAsync();
            _scheduler = null;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The task most recently returned by the debounce scheduler's own <c>ScheduleLint</c> --
    /// <c>internal</c> purely so a test can await a specific lint pass deterministically instead
    /// of guessing at the debounce interval, the same reason
    /// <see cref="DesignerEditContext.PendingAutosave"/> exists.
    /// </summary>
    internal Task PendingLint => _scheduler!.PendingLint;

    private void OnEditContextStateChanged() => _scheduler!.ScheduleLint(EditContext.Draft.Definition);

    /// <summary>
    /// The debounce scheduler's completion callback -- resumes off the renderer's own
    /// synchronization context (the debounce delay itself runs on the thread pool), so this
    /// dispatches back via <see cref="ComponentBase.InvokeAsync(Action)"/> before touching any
    /// component state, the same pattern <c>AriaLiveRegion.OnAnnounced</c> and
    /// <c>FormDesigner.OnEditContextAutosaveFailed</c> already use for their own
    /// off-sync-context event sources.
    /// </summary>
    private void OnLinted(IReadOnlyList<LintResult> results) => InvokeAsync(() => ApplyLintResultsAsync(results));

    /// <summary>
    /// The synchronization-context-bound tail of <see cref="OnLinted"/>, split out purely so the
    /// <c>await</c> that must stay on the renderer's own context through to the
    /// <see cref="ComponentBase.StateHasChanged"/> call after it can carry the suppression
    /// attribute a lambda cannot.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Dispatched via ComponentBase.InvokeAsync onto the renderer's own synchronization context, and must stay on it through the ResultsChanged callback so the StateHasChanged this method calls next is safe to schedule.")]
    private async Task ApplyLintResultsAsync(IReadOnlyList<LintResult> results)
    {
        // A fresh LintResult list that is nonetheless value-equal to the one already showing --
        // FormLinter allocates a new list every pass, even when a mutation touched no rule's own
        // input -- gets none of the work below that only matters for a picture that actually
        // changed (code review fix): no announcement (there is nothing new to speak), no swap
        // of _results (so a reference-based comparison downstream, e.g. DesignerCanvas's own
        // lintResultsChanged check, still sees "unchanged"), and no StateHasChanged for a render
        // that would show the exact same markup. ResultsChanged still fires either way -- this is
        // about this dock's own redundant re-render, not about breaking that bridge to the canvas.
        var unchanged = results.SequenceEqual(_results);

        // The very first completed pass -- _lintRunCount still 0 -- establishes the baseline
        // picture the working draft opened with; an author has not acted on anything yet, so
        // assertively announcing whatever standing findings that draft already carries (e.g. a
        // blocking A11Y-01 left over from before the designer was ever opened) would be an
        // unearned interruption the moment the canvas appears (PRD §11, code review fix). Every
        // pass after this one still announces a real transition exactly as before.
        if (_lintRunCount > 0 && !unchanged)
        {
            AnnounceIfChanged(results);
        }

        _lintRunCount++;

        if (unchanged)
        {
            await ResultsChanged.InvokeAsync(results);
            return;
        }

        _results = results;
        await ResultsChanged.InvokeAsync(results);
        StateHasChanged();
    }

    /// <summary>
    /// Speaks through <see cref="EditContext"/>'s own shared announcer only when the lint picture
    /// actually changed since the last pass -- see this type's own remarks for why a blocking-count
    /// change speaks assertively, a growing total count speaks politely, and anything else (most
    /// mutations, which do not change the lint picture at all) stays silent. Never called for the
    /// first completed pass at all -- see <see cref="ApplyLintResultsAsync"/>'s own guard.
    /// </summary>
    private void AnnounceIfChanged(IReadOnlyList<LintResult> results)
    {
        var previousBlockingCount = _results.Count(r => r.Severity == LintSeverity.Blocking);
        var newBlockingCount = results.Count(r => r.Severity == LintSeverity.Blocking);

        if (newBlockingCount != previousBlockingCount)
        {
            EditContext.Announce(
                Localizer["LinterDockBlockingCountAnnouncement", newBlockingCount].Value,
                AriaLivePoliteness.Assertive);
        }
        else if (results.Count > _results.Count)
        {
            EditContext.Announce(Localizer["LinterDockNewFindingAnnouncement", results.Count].Value);
        }
    }

    private void ToggleExpanded() => _isExpanded = !_isExpanded;

    /// <summary>
    /// The list-position tiebreaker in front of the content keeps two rules that both report the
    /// exact same message and (dangling-reference) <see langword="null"/> node -- e.g.
    /// <c>DanglingReferenceRule</c> reporting the same missing field once for a validation rule's
    /// own target and once for that rule's expression -- keyed distinctly, which Blazor's own
    /// diffing otherwise rejects as a duplicate key.
    /// </summary>
    private static string ResultKey(int index, LintResult result) => $"{index}|{result.RuleId}|{result.NodeId}|{result.Message}";

    private static string SeverityModifier(LintResult result) =>
        result.Severity == LintSeverity.Blocking ? "blocking" : "advisory";

    private static string SeverityLabel(LintResult result) => result.Severity == LintSeverity.Blocking
        ? Localizer["LinterDockSeverityBlocking"].Value
        : Localizer["LinterDockSeverityAdvisory"].Value;

    private static bool CanJumpTo(LintResult result) =>
        result.NodeId is not null && result.PageIndex is not null && result.SectionIndex is not null;

    /// <summary>
    /// Whether <see cref="Fix"/> can deterministically repair <paramref name="result"/> --
    /// currently only <see cref="LintRuleIds.A11y08"/> (a heading that skips a rung), the one rule
    /// this phase gives a one-click fix (PRD §8); every other rule (A11Y-01's missing label,
    /// FR-03's dangling reference, A11Y-06's missing remedy, A11Y-09's link text) needs an
    /// author's own judgment about what to type, so those show jump-to-node only.
    /// </summary>
    private static bool CanFix(LintResult result) =>
        string.Equals(result.RuleId, LintRuleIds.A11y08, StringComparison.Ordinal) && result.NodeId is not null;

    private string NodeLabel(string nodeId)
    {
        var node = EditContext.Draft.Definition.FindNode(nodeId);

        return node is null
            ? nodeId
            : node.Label ?? Localizer["UntitledNodeLabel", Localizer[$"NodeType{node.Type}"].Value].Value;
    }

    /// <summary>
    /// Moves the active page and selection to <paramref name="result"/>'s own node -- navigation
    /// only, per this type's own remarks; never touches <see cref="DesignerEditContext.Draft"/>,
    /// the undo stack, or the autosave scheduler.
    /// </summary>
    private void JumpToNode(LintResult result)
    {
        if (!CanJumpTo(result))
        {
            return;
        }

        var page = EditContext.Draft.Definition.Pages[result.PageIndex!.Value];
        var section = page.Sections[result.SectionIndex!.Value];

        EditContext.Select(DesignerSelection.ForNode(result.NodeId!, page.Id, section.Id, DesignerFocusIntent.JumpedTo));
    }

    /// <summary>
    /// Applies <see cref="LintRuleIds.A11y08"/>'s one-click fix: sets the offending heading's own
    /// <see cref="FormNode.Level"/> to exactly one rung below the nearest preceding heading (or
    /// leaves it alone if this is somehow the first heading after all, which
    /// <see cref="CanFix"/>'s gate never actually lets through) -- the same walk
    /// <c>HeadingLevelRule</c> itself makes, so the fixed level is guaranteed to satisfy the rule
    /// the moment the next lint pass runs. Goes through <see cref="DesignerEditContext.UpdateNode"/>,
    /// so this is a normal, undoable mutation like any other properties-panel edit.
    /// </summary>
    private void Fix(LintResult result)
    {
        if (!CanFix(result) || result.NodeId is not { } nodeId)
        {
            return;
        }

        var definition = EditContext.Draft.Definition;
        var node = definition.FindNode(nodeId);
        var correctedLevel = ComputeCorrectHeadingLevel(definition, nodeId);

        if (node is null || correctedLevel is null)
        {
            return;
        }

        EditContext.UpdateNode(node with { Level = correctedLevel });
    }

    /// <summary>
    /// Replays <c>HeadingLevelRule</c>'s own depth-first heading walk to find the level one rung
    /// below whichever heading immediately precedes <paramref name="nodeId"/>'s own heading --
    /// the level that stops it skipping a rung.
    /// </summary>
    private static int? ComputeCorrectHeadingLevel(FormDefinition definition, string nodeId)
    {
        const int defaultHeadingLevel = 2;
        int? previousLevel = null;

        foreach (var node in definition.EnumerateNodes())
        {
            if (node.Type != NodeType.Heading)
            {
                continue;
            }

            if (string.Equals(node.Id, nodeId, StringComparison.Ordinal))
            {
                return previousLevel is int previous ? previous + 1 : null;
            }

            previousLevel = node.Level ?? defaultHeadingLevel;
        }

        return null;
    }
}
