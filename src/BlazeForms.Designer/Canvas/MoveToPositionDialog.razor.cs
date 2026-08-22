using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Designer;
using BlazeForms.Designer.Internal;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace BlazeForms.Canvas;

/// <summary>
/// The <c>Ctrl+M</c> "move to position" dialog (PRD §4.1): a focus-trapped modal offering a
/// target-section select and a one-based target-position select for the node
/// <see cref="DesignerCanvas"/> opened it for, confirming through the exact same
/// <see cref="DesignerEditContext.MoveNodeToPosition"/> call the drag-and-drop and
/// <c>Alt+↑/↓/←/→</c> paths ultimately share too -- see <see cref="DesignerCanvas"/>'s own remarks
/// on why every reorder path funnels into the same handful of <see cref="DesignerEditContext"/>
/// methods.
/// </summary>
/// <remarks>
/// <para>
/// <b>A fresh instance every open.</b> <see cref="DesignerCanvas"/> mounts this component only
/// while its dialog is showing, tearing it down entirely on close rather than toggling a
/// persistent instance's visibility -- so <see cref="OnInitialized"/> always sees the node's
/// current position fresh, and <see cref="OnAfterRenderAsync"/>'s own <c>firstRender</c> check
/// is always a genuine first render, needing no one-shot "did I already focus/attach" flag
/// of its own the way <see cref="CanvasNodeRow"/> and <see cref="DesignerCanvas"/> both need for
/// their own longer-lived instances.
/// </para>
/// <para>
/// <b>Focus trap.</b> This dialog is <c>role="dialog" aria-modal="true"</c>, labelled by its own
/// title, and moves real DOM focus to its first control (the section select) the moment its first
/// render lands. The collocated <c>MoveToPositionDialog.razor.js</c> module attaches a
/// <c>keydown</c> listener to this dialog's own root element that cycles <c>Tab</c>/<c>Shift+Tab</c>
/// between this dialog's first and last focusable control, calling <c>preventDefault()</c> only
/// when it is actually about to redirect focus itself and never stopping propagation -- the same
/// genuine-platform-gap rationale <see cref="DesignerCanvas"/>'s own scroll-suppression module
/// documents: Blazor's declarative <c>@@onkeydown:preventDefault</c> modifier cannot be made
/// conditional per key value, so expressing "trap <c>Tab</c>, but never touch any other key this
/// same element receives" (typing a position select's first-letter jump, arrow keys changing a
/// select's value, and so on) needs genuine JS. <c>Escape</c>, by contrast, needs no JS at all --
/// Blazor's own <c>@@onkeydown</c> handler already receives it cleanly, and the browser has no
/// default action on it here worth suppressing.
/// </para>
/// <para>
/// <b>Focus destination after closing.</b> Both <see cref="ConfirmAsync"/> and
/// <see cref="CancelAsync"/> only ever raise <see cref="OnClosed"/> -- moving real DOM focus back
/// to the canvas is <see cref="DesignerCanvas"/>'s job, not this dialog's, since by the time this
/// dialog has been torn down there is nothing left here to move focus <em>from</em>. A confirmed
/// move lands on the moved node's own row (via the mutation's <see cref="DesignerFocusIntent.Moved"/>
/// selection, the same signal every other mutation's post-move focus already relies on); a cancel
/// restores focus to the origin row, since a move never changes <see cref="FormNode.Id"/> and so
/// the node this dialog was opened for is exactly the row the roving cursor was already sitting on.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class MoveToPositionDialog : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The static web asset path this component imports its focus-trap JS module from,
    /// following the same <c>_content/{assembly}/{path}</c> convention every collocated Razor
    /// Class Library JS file resolves to. <c>internal</c> so a test can set up the module mock
    /// against the exact path this component requests.
    /// </summary>
    internal const string ModulePath = "./_content/BlazeForms.Designer/Canvas/MoveToPositionDialog.razor.js";

    private readonly string _instanceId = "bf-move-dialog-" + Guid.NewGuid().ToString("n");
    private ElementReference _dialogElement;
    private ElementReference _firstFocusElement;
    private IJSObjectReference? _module;
    private IJSObjectReference? _focusTrapHandle;
    private IReadOnlyList<FormSection> _pageSections = [];
    private string _originalSectionId = string.Empty;
    private int _originalPosition;
    private string _selectedSectionId = string.Empty;
    private int _selectedPosition;
    private bool _disposed;

    /// <summary>
    /// The mutation engine this dialog confirms its move through.
    /// </summary>
    [Parameter, EditorRequired]
    public DesignerEditContext EditContext { get; set; } = default!;

    /// <summary>
    /// The node being moved. Its current page's sections populate the section select; its current
    /// section and one-based position seed both selects' initial values.
    /// </summary>
    [Parameter, EditorRequired]
    public string NodeId { get; set; } = default!;

    /// <summary>
    /// Raised once this dialog should close -- after a confirmed move (<see cref="ConfirmAsync"/>)
    /// or a cancel (<see cref="CancelAsync"/>). Carries no payload: <see cref="DesignerCanvas"/>
    /// reads whatever actually happened straight off <see cref="DesignerEditContext.Selection"/>,
    /// the same signal every other mutation's own post-move focus already relies on.
    /// </summary>
    [Parameter]
    public EventCallback OnClosed { get; set; }

    /// <summary>
    /// Used only to import <see cref="ModulePath"/>'s focus-trap module -- see this type's own
    /// remarks on why closing that platform gap needs genuine JS.
    /// </summary>
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private string SectionSelectId => _instanceId + "-section";

    private string PositionSelectId => _instanceId + "-position";

    private string TitleId => _instanceId + "-title";

    private string NodeLabel
    {
        get
        {
            var node = EditContext.Draft.Definition.FindNode(NodeId)
                ?? throw new InvalidOperationException($"No node '{NodeId}' was found in the current draft.");
            return node.Label ?? Localizer["UntitledNodeLabel", Localizer[$"NodeType{node.Type}"].Value].Value;
        }
    }

    /// <summary>
    /// How many position options the position select currently offers -- one more than the
    /// currently-chosen target section's own node count, excluding the node being moved when that
    /// section is the one it is already in.
    /// </summary>
    private int PositionCount => PositionCountFor(_selectedSectionId);

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        var located = DefinitionMutations.FindNodeLocation(EditContext.Draft.Definition, NodeId)
            ?? throw new InvalidOperationException($"No node '{NodeId}' was found in the current draft.");
        var page = EditContext.Draft.Definition.Pages[located.PageIndex];
        var section = page.Sections[located.SectionIndex];

        _pageSections = page.Sections;
        _originalSectionId = section.Id;
        _originalPosition = located.NodeIndex + 1;
        _selectedSectionId = section.Id;
        _selectedPosition = _originalPosition;
    }

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
        await _firstFocusElement.FocusAsync();
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
    /// solely so a test can prove <see cref="DisposeAsync"/> actually disposes it -- the same
    /// rationale <see cref="DesignerCanvas.HasImportedScrollSuppressionModule"/> gives for itself.
    /// </summary>
    internal bool HasImportedModule => _module is not null;

    private int PositionCountFor(string sectionId)
    {
        var section = _pageSections.FirstOrDefault(candidate => string.Equals(candidate.Id, sectionId, StringComparison.Ordinal));

        if (section is null)
        {
            return 1;
        }

        var countExcludingThisNode = string.Equals(sectionId, _originalSectionId, StringComparison.Ordinal)
            ? section.Nodes.Count - 1
            : section.Nodes.Count;

        return countExcludingThisNode + 1;
    }

    private static string SectionTitle(FormSection section) => section.Title ?? Localizer["UntitledSectionName"].Value;

    /// <summary>
    /// Handles the section select's change -- switching sections defaults the position select to
    /// that section's own end, the same "append" choice <see cref="DesignerCanvas"/>'s own
    /// <c>Alt+←/→</c> path makes when it, too, has no more specific position of its own to offer
    /// (PRD §4.1).
    /// </summary>
    private void OnSectionChanged(string? sectionId)
    {
        if (sectionId is null)
        {
            return;
        }

        _selectedSectionId = sectionId;
        _selectedPosition = PositionCountFor(sectionId);
    }

    private void OnPositionChanged(int position) => _selectedPosition = position;

    /// <summary>
    /// Confirms the move -- via the exact same <see cref="DesignerEditContext.MoveNodeToPosition"/>
    /// the drag-and-drop and <c>Alt+↑/↓/←/→</c> paths ultimately call too -- then closes. A
    /// confirm that leaves both selects exactly where the node already is is a harmless no-op:
    /// <see cref="DesignerEditContext.MoveNodeToPosition"/> itself detects nothing would change
    /// and skips the commit and announcement entirely.
    /// </summary>
    private Task ConfirmAsync()
    {
        EditContext.MoveNodeToPosition(NodeId, _selectedSectionId, _selectedPosition);
        return OnClosed.InvokeAsync();
    }

    /// <summary>
    /// Cancels -- the <c>Esc</c> path, and the visible Cancel button's -- without touching
    /// <see cref="EditContext"/> at all, then closes.
    /// </summary>
    private Task CancelAsync() => OnClosed.InvokeAsync();

    private Task OnDialogKeyDown(KeyboardEventArgs e) =>
        string.Equals(e.Key, "Escape", StringComparison.Ordinal) ? CancelAsync() : Task.CompletedTask;
}
