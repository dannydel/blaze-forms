using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Internal;
using BlazeForms.Markdown;
using BlazeForms.Resources;
using BlazeForms.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace BlazeForms.Components;

/// <summary>
/// One row's move request, carrying the direction as a signed step rather than two separate
/// booleans — <c>-1</c> is "move up", <c>1</c> is "move down" — mirroring
/// <see cref="RepeatingRows.MoveRow"/>'s own <c>delta</c> parameter exactly, so
/// <see cref="RepeatingGroup"/> never has to translate between two different vocabularies for the
/// same concept.
/// </summary>
/// <param name="RowId">
/// The identifier of the row to move.
/// </param>
/// <param name="Delta">
/// The number of positions to move the row by.
/// </param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct RepeatingRowMove(string RowId, int Delta);

/// <summary>
/// Renders one fillable <see cref="NodeType.Repeating"/> group (repeating-groups-plan.md, D-3): an
/// outer <c>fieldset</c>/<c>legend</c> for the group itself, one nested <c>fieldset</c> per row
/// with native <c>&lt;button type="button"&gt;</c> controls to add, remove, and reorder rows, and
/// each row's visible children rendered through the exact same <c>DynamicComponent</c> +
/// component-resolution path <c>FormRenderer</c> uses for every other field — so a host's own
/// per-field override keeps working inside a row exactly as it does at the top level. Peer of
/// <c>ErrorSummary</c>, not a <c>FormFieldBase</c> subclass: a group has no single control and no
/// single answer of its own to bind. Every actual mutation — add, remove, move, and each child's
/// value/blur — is delegated back to <see cref="OnAddRow"/>, <see cref="OnRemoveRow"/>,
/// <see cref="OnMoveRow"/>, and the callbacks <see cref="BuildChildParameters"/> already wires up;
/// this component owns only rendering, focus management, and the announcement text it hands to
/// <see cref="OnAnnounce"/> for <c>FormRenderer</c>'s own shared, visually-hidden row-operation
/// live region.
/// </summary>
/// <remarks>
/// <b>Keyboard and focus (AGENTS.md invariant #4).</b> Add sits after the rows; each row carries
/// Move up, Move down, and Remove. Add → focus moves to the new row's first focusable control —
/// rendered by an arbitrary, host-resolvable field component this component has no
/// <see cref="ElementReference"/> for, so that one case reaches into a tiny collocated JS module
/// (<c>RepeatingGroup.razor.js</c>) to focus it by the row's own container id, the one genuine
/// platform gap here. Remove → focus moves to the next row's Remove button, or the previous row's,
/// or the Add button once no rows remain — all three are elements this component renders directly,
/// so each uses a captured <see cref="ElementReference"/> and no JS. Move → focus needs no explicit
/// handling on a successful move: Blazor's own <c>@@key</c>-based diffing relocates the exact DOM
/// node (and whatever it held focus) rather than recreating it. At
/// <see cref="FormNode.MaxRows"/>/<see cref="FormNode.MinRows"/> — and, for Move, at either end of
/// the row list — the corresponding button stays rendered with <c>aria-disabled="true"</c> and a
/// no-op click that still announces why, never native <c>disabled</c>, which would eject focus
/// from the very button the respondent just used.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class RepeatingGroup : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The static web asset path this component imports its Add-row focus module from, following
    /// the same <c>_content/{assembly}/{path}</c> convention every collocated Razor Class Library
    /// JS file resolves to. <c>internal</c> so this component's own tests can set up the module
    /// mock against the exact path this component requests.
    /// </summary>
    internal const string ModulePath = "./_content/BlazeForms.Renderer/Components/RepeatingGroup.razor.js";

    // Never pruned when a row is removed -- a stale entry for a rowId that no longer exists is
    // harmless dead weight, not a correctness or leak concern: every lookup here is by a row id
    // that is currently live (OnAfterRenderAsync only ever reads _focusRemoveButtonForRowId right
    // after it was set from a still-current Value), and re-adding the same key on the next render
    // that re-creates that row overwrites it for free.
    private readonly Dictionary<string, ElementReference> _removeButtonRefs = new(StringComparer.Ordinal);
    private ElementReference _addButtonRef;
    private string? _focusRemoveButtonForRowId;
    private bool _focusAddButtonOnNextRender;
    private bool _focusNewRowOnNextRender;
    private IJSObjectReference? _module;
    private Task<IJSObjectReference>? _moduleImport;
    private bool _disposed;

    /// <summary>
    /// The repeating group node this instance renders.
    /// </summary>
    [Parameter, EditorRequired]
    public FormNode Node { get; set; } = default!;

    /// <summary>
    /// The group's current answer: its rows, in fill order.
    /// </summary>
    [Parameter, EditorRequired]
    public RepeatingRows Value { get; set; } = default!;

    /// <summary>
    /// The stable DOM id this instance renders its outer <c>fieldset</c> with — the anchor an
    /// error-summary entry for the group-level row-count rule points at.
    /// </summary>
    [Parameter]
    public string FieldId { get; set; } = "";

    /// <summary>
    /// The group-level validation message — today, only the row-count remedy against
    /// <see cref="FormNode.MinRows"/>/<see cref="FormNode.MaxRows"/> — or <see langword="null"/>
    /// when the group carries none.
    /// </summary>
    [Parameter]
    public string? Error { get; set; }

    /// <summary>
    /// Resolves the component type to render for one child node, honoring the host's
    /// <c>IFieldComponentRegistry</c> exactly as <c>FormRenderer</c>'s own top-level resolution
    /// does — <c>FormRenderer.ResolveComponentType</c> is handed straight through as this
    /// delegate.
    /// </summary>
    [Parameter, EditorRequired]
    public Func<FormNode, Type> ResolveChildComponentType { get; set; } = default!;

    /// <summary>
    /// Lists the child ids currently visible within one row, already resolved against the
    /// row-scoped merged view (<c>VisibilityEvaluator.GetVisibleChildIds</c>).
    /// </summary>
    [Parameter, EditorRequired]
    public Func<RepeatingRow, IReadOnlyList<string>> GetVisibleChildIds { get; set; } = default!;

    /// <summary>
    /// Builds the <c>DynamicComponent</c> parameter set for one child within one row — value,
    /// change/blur callbacks routed back into <c>FormRenderer</c>'s own value pipeline, and the
    /// per-(child, row) error, all pre-wired by the caller.
    /// </summary>
    [Parameter, EditorRequired]
    public Func<FormNode, RepeatingRow, Dictionary<string, object>> BuildChildParameters { get; set; } = default!;

    /// <summary>
    /// Raised when the respondent activates Add and the group is not already at
    /// <see cref="FormNode.MaxRows"/>.
    /// </summary>
    [Parameter, EditorRequired]
    public EventCallback OnAddRow { get; set; }

    /// <summary>
    /// Raised with the row's own id when the respondent activates that row's Remove and the group
    /// is not already at <see cref="FormNode.MinRows"/>.
    /// </summary>
    [Parameter, EditorRequired]
    public EventCallback<string> OnRemoveRow { get; set; }

    /// <summary>
    /// Raised when the respondent activates a row's Move up or Move down and the move would
    /// actually change its position.
    /// </summary>
    [Parameter, EditorRequired]
    public EventCallback<RepeatingRowMove> OnMoveRow { get; set; }

    /// <summary>
    /// Raised with the localized text every row-mutating action (including a blocked one at
    /// min/max) announces — the caller renders it into its own shared, visually-hidden
    /// <c>aria-live="polite"</c> region, separate from the calc announcer.
    /// </summary>
    [Parameter, EditorRequired]
    public EventCallback<string> OnAnnounce { get; set; }

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    private static IStringLocalizer<RendererStrings> Localizer => RendererLocalization.Shared;

    /// <summary>
    /// The singular noun this group's rows are named by (PRD §5): <see cref="FormNode.ItemLabel"/>
    /// when the author set one, falling back to the group's own <see cref="FormNode.Label"/>.
    /// </summary>
    private string ItemNoun => Node.ItemLabel ?? Node.Label ?? "";

    private bool IsAtMaxRows => Node.MaxRows is int max && Value.Rows.Count >= max;

    private bool IsAtMinRows => Node.MinRows is int min && Value.Rows.Count <= min;

    private string HelpElementId => $"{FieldId}-help";

    private string ErrorElementId => $"{FieldId}-error";

    private string RowDomId(string rowId) => $"{FieldId}-row-{rowId}";

    /// <summary>
    /// Builds the outer <c>fieldset</c>'s <c>aria-describedby</c> value: the help element id when
    /// <see cref="FormNode.Help"/> is set, the error element id when <see cref="Error"/> is set,
    /// both, or <see langword="null"/> to omit the attribute entirely — the same combinations
    /// <c>FormFieldBase.BuildDescribedBy</c> covers for an ordinary field.
    /// </summary>
    private string? BuildDescribedBy()
    {
        var hasHelp = !string.IsNullOrWhiteSpace(Node.Help);
        var hasError = !string.IsNullOrWhiteSpace(Error);

        return (hasHelp, hasError) switch
        {
            (true, true) => $"{HelpElementId} {ErrorElementId}",
            (true, false) => HelpElementId,
            (false, true) => ErrorElementId,
            (false, false) => null,
        };
    }

    private FormNode? FindChild(string childId) =>
        Node.Children.FirstOrDefault(child => string.Equals(child.Id, childId, StringComparison.Ordinal));

    private string RowLegend(int ordinal) => Localizer["RepeatingRowLegend", ItemNoun, ordinal].Value;

    private string AddLabel => Localizer["RepeatingAddButtonLabel", ItemNoun].Value;

    private string RemoveLabel(int ordinal) => Localizer["RepeatingRemoveButtonLabel", ItemNoun, ordinal].Value;

    private string MoveUpLabel(int ordinal) => Localizer["RepeatingMoveUpButtonLabel", ItemNoun, ordinal].Value;

    private string MoveDownLabel(int ordinal) => Localizer["RepeatingMoveDownButtonLabel", ItemNoun, ordinal].Value;

    /// <summary>
    /// <see cref="FormNode.Help"/> rendered through Core's shared safe-Markdown pipeline
    /// (AGENTS.md invariant #6) — the same pattern every shipped field component follows for its
    /// own help text.
    /// </summary>
    private MarkupString HelpMarkup => new(SafeMarkdown.ToHtml(Node.Help).Value);

    /// <inheritdoc />
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next focus call.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusNewRowOnNextRender)
        {
            _focusNewRowOnNextRender = false;

            if (Value.Rows.Count > 0)
            {
                var module = await GetModuleAsync();
                await module.InvokeVoidAsync("focusFirstControlIn", RowDomId(Value.Rows[^1].RowId));
            }
        }

        if (_focusRemoveButtonForRowId is { } rowId)
        {
            _focusRemoveButtonForRowId = null;

            if (_removeButtonRefs.TryGetValue(rowId, out var buttonRef))
            {
                await buttonRef.FocusAsync();
            }
        }

        if (_focusAddButtonOnNextRender)
        {
            _focusAddButtonOnNextRender = false;
            await _addButtonRef.FocusAsync();
        }
    }

    /// <summary>
    /// Handles the Add button: a no-op (beyond announcing why) at <see cref="FormNode.MaxRows"/>,
    /// otherwise arms the new-row focus request before delegating the actual mutation to
    /// <see cref="OnAddRow"/> — the new row's own id is not known until that mutation's result
    /// flows back down through <see cref="Value"/>, so <see cref="OnAfterRenderAsync"/> reads it
    /// from the parameter this instance was re-rendered with, never from a value captured here.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context so it can safely schedule the next render.")]
    private async Task HandleAddClick()
    {
        if (IsAtMaxRows)
        {
            await OnAnnounce.InvokeAsync(Localizer["RepeatingAddBlockedAnnouncement", ItemNoun, Node.MaxRows!.Value].Value);
            return;
        }

        _focusNewRowOnNextRender = true;
        await OnAnnounce.InvokeAsync(BuildAddedAnnouncement());
        await OnAddRow.InvokeAsync();
    }

    /// <summary>
    /// Handles one row's Remove button: a no-op (beyond announcing why) at
    /// <see cref="FormNode.MinRows"/>, otherwise computes the post-removal focus target from the
    /// row's position in the <em>current</em> <see cref="Value"/> — the next row's Remove button,
    /// or the previous row's, or the Add button once none remain — before delegating the removal
    /// itself to <see cref="OnRemoveRow"/>.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context so it can safely schedule the next render.")]
    private async Task HandleRemoveClick(string rowId, int index)
    {
        if (IsAtMinRows)
        {
            await OnAnnounce.InvokeAsync(Localizer["RepeatingRemoveBlockedAnnouncement", ItemNoun, Node.MinRows!.Value].Value);
            return;
        }

        var rows = Value.Rows;

        if (index + 1 < rows.Count)
        {
            _focusRemoveButtonForRowId = rows[index + 1].RowId;
        }
        else if (index - 1 >= 0)
        {
            _focusRemoveButtonForRowId = rows[index - 1].RowId;
        }
        else
        {
            _focusAddButtonOnNextRender = true;
        }

        await OnAnnounce.InvokeAsync(BuildRemovedAnnouncement(index));
        await OnRemoveRow.InvokeAsync(rowId);
    }

    /// <summary>
    /// Handles one row's Move up/down button. A move that would land the row outside the list —
    /// the same bounds rule <see cref="RepeatingRows.MoveRow"/> itself applies — never reaches
    /// <see cref="OnMoveRow"/> for a click that would change nothing, but still announces why, for
    /// the same screen-reader parity a blocked Add/Remove already gets (<see cref="HandleAddClick"/>,
    /// <see cref="HandleRemoveClick"/>) rather than leaving a boundary press silently unconfirmed.
    /// <paramref name="rowId"/> not matching any current row at all is the one true no-op left —
    /// defensive against a stale button click racing a row's own removal, never reachable through
    /// this component's own rendered markup. No explicit focus handling on a successful move: the
    /// pressed button travels with its row via <c>@@key</c>, so it keeps focus without this
    /// component doing anything.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context so it can safely schedule the next render.")]
    private async Task HandleMoveClick(string rowId, int delta)
    {
        var index = IndexOf(rowId);

        if (index < 0)
        {
            return;
        }

        var target = index + delta;

        if (target < 0 || target >= Value.Rows.Count)
        {
            await OnAnnounce.InvokeAsync(BuildBlockedMoveAnnouncement(index, delta));
            return;
        }

        await OnAnnounce.InvokeAsync(Localizer["RepeatingRowMovedAnnouncement", ItemNoun, target + 1, Value.Rows.Count].Value);
        await OnMoveRow.InvokeAsync(new RepeatingRowMove(rowId, delta));
    }

    /// <summary>
    /// Builds the blocked-move announcement: <paramref name="delta"/> decides which direction was
    /// blocked, and <paramref name="index"/> — the row's own current, unchanged 0-based position —
    /// becomes the 1-based ordinal the message names.
    /// </summary>
    private string BuildBlockedMoveAnnouncement(int index, int delta) => delta < 0
        ? Localizer["RepeatingMoveBlockedUpAnnouncement", ItemNoun, index + 1].Value
        : Localizer["RepeatingMoveBlockedDownAnnouncement", ItemNoun, index + 1].Value;

    private int IndexOf(string rowId)
    {
        for (var i = 0; i < Value.Rows.Count; i++)
        {
            if (string.Equals(Value.Rows[i].RowId, rowId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private string BuildAddedAnnouncement() =>
        Localizer["RepeatingRowAddedAnnouncement", ItemNoun, Value.Rows.Count + 1, CountSuffix(Value.Rows.Count + 1)].Value;

    private string BuildRemovedAnnouncement(int removedIndex) =>
        Localizer["RepeatingRowRemovedAnnouncement", ItemNoun, removedIndex + 1, CountSuffix(Value.Rows.Count - 1)].Value;

    private string CountSuffix(int count) => Node.MaxRows is int max
        ? Localizer["RepeatingRowCountOfMax", count, max].Value
        : Localizer["RepeatingRowCount", count].Value;

    /// <summary>
    /// Returns the shared module-import task, starting it on the first call and handing every
    /// later call the same in-flight (or completed) task — the same caching rationale
    /// <c>FormSubmissionView.GetModuleAsync</c> gives for its own export module.
    /// </summary>
    private Task<IJSObjectReference> GetModuleAsync() => _moduleImport ??= ImportModuleAsync();

    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Called only from GetModuleAsync, which is reached only from OnAfterRenderAsync, which must itself resume on the renderer's synchronization context.")]
    private async Task<IJSObjectReference> ImportModuleAsync()
    {
        var module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);

        if (_disposed)
        {
            await module.DisposeAsync();
            return module;
        }

        _module = module;
        return module;
    }

    /// <summary>
    /// Whether the JS module has been imported. <c>internal</c>, not <c>private</c>, solely so
    /// this component's own tests can prove it is disposed — the same rationale
    /// <c>FormSubmissionView.HasImportedModule</c> gives.
    /// </summary>
    internal bool HasImportedModule => _module is not null;

    /// <inheritdoc />
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

        if (_module is not null)
        {
            await _module.DisposeAsync();
            _module = null;
        }

        GC.SuppressFinalize(this);
    }
}
