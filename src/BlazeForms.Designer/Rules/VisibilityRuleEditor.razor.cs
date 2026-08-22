using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Designer;
using BlazeForms.Designer.Internal;
using BlazeForms.Expressions;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace BlazeForms.Rules;

/// <summary>
/// The focus-trapped dialog that edits one node's own <see cref="FormNode.VisibleWhen"/> (PRD
/// §4.1, §6): an All/Any (<see cref="ConditionJoin"/>) toggle plus a list of <see cref="ConditionRow"/>s,
/// with add/remove, mirroring <c>MoveToPositionDialog</c>'s own focus-trap and Esc-cancel contract.
/// <see cref="Properties.PropertiesPanel"/> is this dialog's only intended host, mounting it only
/// while it is showing, the same "a fresh instance every open" pattern
/// <c>MoveToPositionDialog</c>'s own remarks explain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cycle rejection is the one thing that does not close this dialog (PRD §6).</b>
/// <see cref="ApplyAsync"/> builds the candidate <see cref="ConditionGroup"/> from this dialog's own
/// working state and calls <see cref="ExpressionDependencyAnalysis.WouldCreateCycle"/> against it
/// before ever touching <see cref="EditContext"/>. A cycle leaves <see cref="DesignerEditContext.Draft"/> entirely
/// untouched and instead renders a <c>role="alert"</c> naming the cycle as an arrow-joined chain of
/// each node's own label, moving real DOM focus there so a screen-reader user hears the rejection
/// immediately rather than having to discover it by re-reading the form. Only a candidate that
/// clears that check -- or an author clearing every condition, which can never introduce a cycle --
/// ever reaches <see cref="DesignerEditContext.UpdateNode"/>, and only then does this dialog raise
/// <see cref="OnClosed"/>.
/// </para>
/// <para>
/// <b>One commit, on Apply, never per control (AGENTS.md render discipline).</b> Every edit here --
/// the join toggle, every <see cref="ConditionRow"/>'s own field/operator/value, add, remove -- only
/// ever mutates this dialog's own working lists; nothing reaches <see cref="EditContext"/> until
/// <see cref="ApplyAsync"/> runs, so a rule with several conditions never floods the undo stack
/// (PRD §4.1's depth-50 cap) with one entry per keystroke.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class VisibilityRuleEditor : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The static web asset path this component imports its focus-trap JS module from. See
    /// <c>MoveToPositionDialog.ModulePath</c>'s own remarks for the convention;
    /// <see langword="internal"/> so a test can set up the module mock against the exact path this
    /// component requests.
    /// </summary>
    internal const string ModulePath = "./_content/BlazeForms.Designer/Rules/VisibilityRuleEditor.razor.js";

    private readonly string _instanceId = "bf-visibility-dialog-" + Guid.NewGuid().ToString("n");
    private ElementReference _dialogElement;
    private ElementReference _firstFocusElement;
    private ElementReference _alertElement;
    private ElementReference _addButtonElement;
    private IJSObjectReference? _module;
    private IJSObjectReference? _focusTrapHandle;
    private IReadOnlyList<FormNode> _fields = [];
    private List<Condition> _conditions = [];
    private ConditionJoin _join;
    private IReadOnlyList<string>? _cyclePath;
    private bool _focusAlertOnNextRender;
    private int? _focusConditionIndex;
    private bool _focusAddButtonOnNextRender;
    private bool _disposed;

    /// <summary>
    /// The mutation engine this dialog applies its rule through.
    /// </summary>
    [Parameter, EditorRequired]
    public DesignerEditContext EditContext { get; set; } = default!;

    /// <summary>
    /// The node whose <see cref="FormNode.VisibleWhen"/> is being edited. Its current rule (or the
    /// absence of one) seeds this dialog's own working state.
    /// </summary>
    [Parameter, EditorRequired]
    public string NodeId { get; set; } = default!;

    /// <summary>
    /// Raised once this dialog should close -- after a successful apply (including one that clears
    /// the rule) or a cancel. Never raised after a cycle rejection, since that leaves the dialog
    /// open for the author to fix. Carries no payload, the same reason
    /// <c>MoveToPositionDialog.OnClosed</c> does not either.
    /// </summary>
    [Parameter]
    public EventCallback OnClosed { get; set; }

    /// <summary>
    /// Used only to import <see cref="ModulePath"/>'s focus-trap module.
    /// </summary>
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private string TitleId => _instanceId + "-title";

    private string JoinGroupName => _instanceId + "-join";

    private string NodeLabel => FieldLabelFor(RequireNode());

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        var node = RequireNode();
        _join = node.VisibleWhen?.Join ?? ConditionJoin.All;
        _conditions = [.. node.VisibleWhen?.Conditions ?? []];

        // Boundary-aware (repeating-groups-plan.md, Increment C): a child inside a repeating
        // group offers its own siblings plus every top-level field; a top-level node excludes
        // every group's children entirely -- the authoring-time counterpart to the linter's
        // blocking FR-04 rule.
        var inputFields = EditContext.Draft.Definition.EnumerateNodes().Where(candidate => FormSchema.IsInputNode(candidate.Type));
        _fields = RuleFieldBoundary.Filter(EditContext.Draft.Definition, NodeId, inputFields);
    }

    /// <summary>
    /// Clears <see cref="_focusConditionIndex"/> the moment this render has gone out --
    /// <see cref="ConditionRow"/>'s own <c>OnParametersSet</c> has already latched whatever
    /// <see cref="ConditionRow.RequestFocus"/> value this render handed it by the time this runs,
    /// so clearing here (rather than in <see cref="OnAfterRenderAsync"/>) is safe and stops the
    /// same index from re-stealing focus on some later, unrelated render -- the same one-shot
    /// reset <c>Canvas.DesignerCanvas.OnAfterRender</c> uses for its own row focus signal.
    /// </summary>
    protected override void OnAfterRender(bool firstRender) => _focusConditionIndex = null;

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _focusTrapHandle = await _module.InvokeAsync<IJSObjectReference>("attachFocusTrap", _dialogElement);
            await _firstFocusElement.FocusAsync();
        }

        if (_focusAlertOnNextRender)
        {
            _focusAlertOnNextRender = false;
            await _alertElement.FocusAsync();
        }

        if (_focusAddButtonOnNextRender)
        {
            _focusAddButtonOnNextRender = false;
            await _addButtonElement.FocusAsync();
        }
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
    /// rationale <c>MoveToPositionDialog.HasImportedModule</c> gives for itself.
    /// </summary>
    internal bool HasImportedModule => _module is not null;

    /// <summary>
    /// The cycle path a rejected <see cref="ApplyAsync"/> most recently reported, resolved to node
    /// labels and arrow-joined. <c>internal</c> purely so a test can assert on it directly rather
    /// than re-parsing the rendered alert's text.
    /// </summary>
    internal string? CycleErrorText => _cyclePath is { Count: > 0 } path ? CyclePathText(path) : null;

    /// <summary>
    /// Appends a new condition, defaulting to the first candidate field, and asks its own row's
    /// Field select to take focus once this render lands (WCAG 2.4.3).
    /// </summary>
    private void AddCondition()
    {
        var fallbackField = _fields.Count > 0 ? _fields[0].Id : string.Empty;
        _conditions.Add(new Condition { Field = fallbackField, Operator = ConditionOperator.Is });
        _focusConditionIndex = _conditions.Count - 1;
    }

    /// <summary>
    /// Removes the condition at <paramref name="index"/> and moves focus to a stable neighbour
    /// (PRD §11, WCAG 2.4.3): the previous row's own Field select (or, removing the first row,
    /// whichever row now sits at index zero -- <see cref="Math.Max(int, int)"/> against
    /// <c>index - 1</c> covers both), or, once no row survives at all, the add-condition button.
    /// </summary>
    private void RemoveCondition(int index)
    {
        _conditions.RemoveAt(index);

        if (_conditions.Count > 0)
        {
            _focusConditionIndex = Math.Max(0, index - 1);
        }
        else
        {
            _focusAddButtonOnNextRender = true;
        }
    }

    private void UpdateCondition(int index, Condition condition) => _conditions[index] = condition;

    /// <summary>
    /// Builds the candidate rule from this dialog's own working state and either applies it,
    /// clears the rule (no conditions left), or rejects it with the named cycle -- see this type's
    /// own remarks.
    /// </summary>
    private Task ApplyAsync()
    {
        _cyclePath = null;
        var node = RequireNode();

        if (_conditions.Count == 0)
        {
            EditContext.UpdateNode(node with { VisibleWhen = null });
            return OnClosed.InvokeAsync();
        }

        var candidate = new ConditionGroup { Join = _join, Conditions = _conditions };

        if (ExpressionDependencyAnalysis.WouldCreateCycle(EditContext.Draft.Definition, NodeId, candidate, out var cyclePath))
        {
            _cyclePath = cyclePath;
            _focusAlertOnNextRender = true;
            return Task.CompletedTask;
        }

        EditContext.UpdateNode(node with { VisibleWhen = candidate });
        return OnClosed.InvokeAsync();
    }

    /// <summary>
    /// Cancels -- the Esc path, and the visible Cancel button's -- without touching
    /// <see cref="EditContext"/> at all, then closes.
    /// </summary>
    private Task CancelAsync() => OnClosed.InvokeAsync();

    private Task OnDialogKeyDown(KeyboardEventArgs e) =>
        string.Equals(e.Key, "Escape", StringComparison.Ordinal) ? CancelAsync() : Task.CompletedTask;

    private FormNode RequireNode() =>
        EditContext.Draft.Definition.FindNode(NodeId)
            ?? throw new InvalidOperationException($"No node '{NodeId}' was found in the current draft.");

    private string CyclePathText(IReadOnlyList<string> path) => string.Join(" → ", path.Select(ResolveLabel));

    private string ResolveLabel(string nodeId)
    {
        var node = EditContext.Draft.Definition.FindNode(nodeId);
        return node is null ? nodeId : FieldLabelFor(node);
    }

    private static string FieldLabelFor(FormNode node) =>
        node.Label ?? Localizer["UntitledNodeLabel", Localizer[$"NodeType{node.Type}"].Value].Value;
}
