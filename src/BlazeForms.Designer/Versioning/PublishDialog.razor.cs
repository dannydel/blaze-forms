using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Designer;
using BlazeForms.Hosting;
using BlazeForms.Internal;
using BlazeForms.Linting;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace BlazeForms.Versioning;

/// <summary>
/// The publish gate (PRD §7): a focus-trapped modal that lints the working draft on OPEN and
/// again on CONFIRM before ever calling <see cref="IFormDefinitionStore.PublishAsync"/> — the
/// designer, not the store, is the sole in-app publish gate (<see cref="FormLinter"/> itself
/// never runs inside <see cref="IFormDefinitionStore"/>). A blocking result disables Confirm
/// entirely and lists every one of <see cref="LintResultExtensions.Blocking"/>'s findings with a
/// jump-to-node action; once clean, Confirm stays disabled until the author types a non-empty
/// change note, then calls <see cref="Store"/>'s own <see cref="IFormDefinitionStore.PublishAsync"/>
/// and raises <see cref="OnPublished"/> with the version it returns.
/// </summary>
/// <remarks>
/// <para>
/// <b>Closing the edit-after-open TOCTOU.</b> <see cref="OnInitialized"/> lints once, purely to
/// decide what this dialog's very first render shows; <see cref="ConfirmAsync"/> lints again,
/// from scratch, before it ever inspects <see cref="CanConfirm"/> — so a blocking issue an author
/// somehow introduces after this dialog opened (the underlying canvas is unreachable while this
/// modal holds focus, but a host embedding this component in some other flow might still mutate
/// the same <see cref="DesignerEditContext"/> concurrently) is caught at the one moment it
/// actually matters, rather than trusting a picture that can be stale by the time Confirm runs.
/// </para>
/// <para>
/// <b>A fresh instance every open.</b> <see cref="FormDesigner"/> mounts this component only
/// while its own dialog is showing, tearing it down entirely on close — the same "no persistent
/// instance to toggle" shape <c>MoveToPositionDialog</c> documents for itself, for the same
/// reasons; <see cref="OnInitialized"/>'s lint pass is always a genuine first look.
/// </para>
/// <para>
/// <b>Focus trap.</b> This dialog is <c>role="dialog" aria-modal="true"</c>, labelled by its own
/// title, and moves real DOM focus to its Cancel button the moment its first render lands —
/// defaulting to the safe action, not whichever control happens to render first, for the same
/// reason <c>DeleteProtectionDialog</c> defaults to its own Cancel button: a stray <c>Enter</c>
/// reaching whichever control already has focus must never itself publish anything. The
/// collocated <c>PublishDialog.razor.js</c> module cycles <c>Tab</c>/<c>Shift+Tab</c> among this
/// dialog's own focusable controls, the same genuine-platform-gap rationale every other trapped
/// dialog in this project documents for its own trap. <c>Escape</c> needs no JS at all.
/// </para>
/// <para>
/// <b>Focus destination after closing.</b> Both a successful publish and a cancel only ever raise
/// <see cref="OnClosed"/> — moving real DOM focus back to the toolbar's own Publish button is
/// <see cref="FormDesigner"/>'s job, the same split every other dialog in this project documents
/// for itself.
/// </para>
/// </remarks>
public partial class PublishDialog : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The static web asset path this component imports its focus-trap JS module from, following
    /// the same <c>_content/{assembly}/{path}</c> convention every collocated Razor Class Library
    /// JS file resolves to. <c>internal</c> so a test can set up the module mock against the exact
    /// path this component requests.
    /// </summary>
    internal const string ModulePath = "./_content/BlazeForms.Designer/Versioning/PublishDialog.razor.js";

    private readonly string _instanceId = "bf-publish-dialog-" + Guid.NewGuid().ToString("n");
    private ElementReference _dialogElement;
    private ElementReference _cancelButtonElement;
    private IJSObjectReference? _module;
    private IJSObjectReference? _focusTrapHandle;
    private IReadOnlyList<LintResult> _lintResults = [];
    private string _changeNote = string.Empty;
    private bool _disposed;
    private bool _publishing;

    /// <summary>
    /// The mutation engine holding the draft this dialog lints and publishes.
    /// </summary>
    [Parameter, EditorRequired]
    public DesignerEditContext EditContext { get; set; } = default!;

    /// <summary>
    /// Where this dialog publishes the draft to.
    /// </summary>
    [Parameter, EditorRequired]
    public IFormDefinitionStore Store { get; set; } = default!;

    /// <summary>
    /// The host-supplied display name of the author publishing this draft. <see langword="null"/>
    /// falls back to <see cref="Localizer"/>'s own localized "Unknown" (PRD §7 requires an author
    /// on every publish; a host that has not wired one up yet must not be blocked from publishing
    /// over it).
    /// </summary>
    [Parameter]
    public string? Author { get; set; }

    /// <summary>
    /// Raised once this dialog should close — after a successful publish or a cancel (<c>Esc</c>
    /// or the visible Cancel button). Carries no payload; a successful publish's own payload
    /// travels on <see cref="OnPublished"/> instead, raised immediately before this.
    /// </summary>
    [Parameter]
    public EventCallback OnClosed { get; set; }

    /// <summary>
    /// Raised with the newly published version once <see cref="Store"/>'s own
    /// <see cref="IFormDefinitionStore.PublishAsync"/> returns — <see cref="FormDesigner"/>'s own
    /// hook for tearing down the now-consumed draft's <see cref="DesignerEditContext"/> and
    /// reloading a fresh one, and for bubbling the same version out through its own
    /// <see cref="FormDesigner.OnPublished"/>. Never raised on a cancel.
    /// </summary>
    [Parameter]
    public EventCallback<FormVersion> OnPublished { get; set; }

    /// <summary>
    /// Used only to import <see cref="ModulePath"/>'s focus-trap module.
    /// </summary>
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private string TitleId => _instanceId + "-title";

    private string BlockReasonId => _instanceId + "-block-reason";

    private string NoteTextareaId => _instanceId + "-note";

    private string NoteRequiredHintId => _instanceId + "-note-required-hint";

    /// <summary>
    /// The blocking-and-non-empty-note gate <see cref="ConfirmAsync"/> itself checks before ever
    /// reaching <see cref="Store"/>: <see langword="false"/> whenever <see cref="_lintResults"/>
    /// carries a blocking result (no change note is even shown in that state), and otherwise
    /// <see langword="false"/> until the change note carries something other than whitespace.
    /// Deliberately independent of <see cref="_publishing"/> — <see cref="ConfirmAsync"/> sets
    /// that flag before this gate ever runs, so folding it in here would make the gate see its
    /// own in-flight publish as itself blocking. <see cref="IsConfirmDisabled"/> is the one
    /// Confirm's own <c>disabled</c> attribute reflects.
    /// </summary>
    private bool CanConfirm => !_lintResults.HasBlockingIssues() && !string.IsNullOrWhiteSpace(_changeNote);

    /// <summary>
    /// What Confirm's own <c>disabled</c> attribute reflects: unavailable whenever
    /// <see cref="CanConfirm"/> is <see langword="false"/>, or a publish is already in flight
    /// (<see cref="_publishing"/>) — so a second click landing mid-<c>await</c>, before the first
    /// click's own disabling render has reached the DOM, sees Confirm as unavailable too, not just
    /// <see cref="ConfirmAsync"/>'s own re-entry guard.
    /// </summary>
    private bool IsConfirmDisabled => _publishing || !CanConfirm;

    /// <summary>
    /// The id Confirm's own <c>aria-describedby</c> reflects, giving a screen-reader user a
    /// programmatic reason for a disabled Confirm rather than silence: <see cref="BlockReasonId"/>
    /// while <see cref="_lintResults"/> is blocking, <see cref="NoteRequiredHintId"/> once lint is
    /// clean but the change note is still blank, and <see langword="null"/> the instant Confirm is
    /// actually enabled.
    /// </summary>
    private string? ConfirmDescribedById => _lintResults.HasBlockingIssues()
        ? BlockReasonId
        : string.IsNullOrWhiteSpace(_changeNote) ? NoteRequiredHintId : null;

    private string ResolvedAuthor => string.IsNullOrWhiteSpace(Author) ? Localizer["PublishDialogUnknownAuthor"].Value : Author;

    /// <summary>
    /// The lint pass this dialog's very first render shows. <c>internal</c>, not <c>private</c>,
    /// solely so a test can assert the blocking picture without parsing rendered markup.
    /// </summary>
    internal IReadOnlyList<LintResult> LintResults => _lintResults;

    /// <inheritdoc/>
    protected override void OnInitialized() => _lintResults = FormLinter.CreateDefault().Lint(EditContext.Draft.Definition);

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
        _focusTrapHandle = await _module.InvokeAsync<IJSObjectReference>("attachFocusTrap", _dialogElement);
        await _cancelButtonElement.FocusAsync();
    }

    /// <summary>
    /// Detaches the focus-trap listener and disposes the imported module. Safe to call more than
    /// once.
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

        if (_focusTrapHandle is not null)
        {
            await _focusTrapHandle.InvokeVoidAsync("dispose");
            await _focusTrapHandle.DisposeAsync();
            _focusTrapHandle = null;
        }

        if (_module is not null)
        {
            await _module.DisposeAsync();
            _module = null;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Whether the focus-trap JS module has been imported. <c>internal</c>, not <c>private</c>,
    /// solely so a test can prove <see cref="DisposeAsync"/> actually disposes it — the same
    /// rationale every other trapped dialog in this project gives for itself.
    /// </summary>
    internal bool HasImportedModule => _module is not null;

    private void OnChangeNoteChanged(string? value) => _changeNote = value ?? string.Empty;

    /// <summary>
    /// Re-lints from scratch — this dialog's own TOCTOU close — and only then checks
    /// <see cref="CanConfirm"/>: a blocking issue introduced since <see cref="OnInitialized"/>
    /// ran flips <see cref="_lintResults"/> back to the blocking view (replacing whatever change
    /// note the author had typed) instead of ever reaching <see cref="Store"/>. Once clean, this
    /// explicitly saves <see cref="EditContext"/>'s own current draft before publishing it —
    /// <see cref="IFormDefinitionStore.PublishAsync"/> publishes whatever draft the store already
    /// holds, not a definition this call passes in, and <c>DesignerEditContext</c>'s own autosave
    /// debounces every save after the first (<c>Internal.AutosaveScheduler</c>'s own remarks), so
    /// without this the store could still be holding an older snapshot than what is on screen the
    /// instant Confirm is pressed — or, for a draft that has never yet been edited at all, no
    /// snapshot whatsoever. Publishing itself consumes the draft, so this never touches
    /// <see cref="EditContext"/> again once <see cref="Store"/> hands back the new version — that
    /// version, and the now-stale draft it replaced, are <see cref="FormDesigner"/>'s own concern
    /// from here.
    /// </summary>
    /// <remarks>
    /// <b>Re-entry guard.</b> Under an async host store, both of the <c>await</c>s below leave
    /// Confirm's own click handler suspended for at least one render cycle, and this dialog's
    /// markup keeps that button visible and enabled (<see cref="IsConfirmDisabled"/> reflects
    /// <see cref="_publishing"/> only once a render actually lands) — a fast double-click, or any
    /// re-entrant call, must not run the save-then-publish sequence twice. <see cref="_publishing"/>
    /// is set synchronously, as the very first statement, before the re-lint or anything else runs
    /// again; a second call made while the first is still executing is guaranteed to observe it
    /// already set, and returns immediately. It is cleared in the <c>finally</c> below only when
    /// this call did NOT reach a successful publish, because a successful publish raises
    /// <see cref="OnClosed"/> and this instance is torn down regardless — resetting the flag on
    /// that path would serve no one and would let a lingering re-render briefly re-enable a button
    /// about to vanish. <c>internal</c>, not <c>private</c>, solely so a test can call it directly
    /// and prove the guard deterministically, the same rationale <c>FormRenderer.SubmitAsync</c>
    /// gives for its own internal test seam.
    /// </remarks>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "An event-callback handler resumes on the renderer's synchronization context, and must stay on it through to the OnClosed.InvokeAsync call at the end.")]
    internal async Task ConfirmAsync()
    {
        if (_publishing)
        {
            return;
        }

        _publishing = true;
        var succeeded = false;

        try
        {
            _lintResults = FormLinter.CreateDefault().Lint(EditContext.Draft.Definition);

            if (!CanConfirm)
            {
                return;
            }

            await Store.SaveDraftAsync(EditContext.Draft);
            var published = await Store.PublishAsync(EditContext.Draft.FormId, _changeNote, ResolvedAuthor);
            succeeded = true;
            await OnPublished.InvokeAsync(published);
            await OnClosed.InvokeAsync();
        }
        finally
        {
            if (!succeeded)
            {
                _publishing = false;
            }
        }
    }

    /// <summary>
    /// Cancels — the <c>Esc</c> path, and the visible Cancel button's — without touching
    /// <see cref="EditContext"/> or <see cref="Store"/> at all, then closes.
    /// </summary>
    private Task CancelAsync() => OnClosed.InvokeAsync();

    private Task OnDialogKeyDown(KeyboardEventArgs e) =>
        string.Equals(e.Key, "Escape", StringComparison.Ordinal) ? CancelAsync() : Task.CompletedTask;

    private static bool CanJumpTo(LintResult result) =>
        result.NodeId is not null && result.PageIndex is not null && result.SectionIndex is not null;

    private string NodeLabel(string nodeId)
    {
        var node = EditContext.Draft.Definition.FindNode(nodeId);

        return node is null
            ? nodeId
            : node.Label ?? Localizer["UntitledNodeLabel", Localizer[$"NodeType{node.Type}"].Value].Value;
    }

    /// <summary>
    /// Moves the active page and selection to a blocking finding's own node — the same
    /// navigation-only <see cref="DesignerEditContext.Select"/> call <c>LinterDock</c>'s own
    /// jump-to-node action makes (see that type's remarks for why this is not an undoable edit) —
    /// then closes this dialog, since the author is about to be looking at the canvas instead.
    /// </summary>
    private Task JumpToNodeAsync(LintResult result)
    {
        if (!CanJumpTo(result))
        {
            return Task.CompletedTask;
        }

        var page = EditContext.Draft.Definition.Pages[result.PageIndex!.Value];
        var section = page.Sections[result.SectionIndex!.Value];

        EditContext.Select(DesignerSelection.ForNode(result.NodeId!, page.Id, section.Id, DesignerFocusIntent.JumpedTo));
        return OnClosed.InvokeAsync();
    }

    private static string ResultKey(int index, LintResult result) => $"{index}|{result.RuleId}|{result.NodeId}|{result.Message}";
}
