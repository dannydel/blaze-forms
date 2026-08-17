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
/// The focus-trapped dialog that edits one <see cref="Definitions.NodeType.Calc"/> node's own
/// <see cref="FormNode.Calculation"/> (PRD §4.1, §5, §13): an operation (<see cref="CalcOperation"/>)
/// select, a format (<see cref="CalcFormat"/>) select, and a list of <see cref="CalcOperandRow"/>s,
/// with add/remove — the calc sibling of <see cref="VisibilityRuleEditor"/>, following its exact
/// contract: focus trap, working-state + one commit on Apply, Esc-cancel, and cycle rejection.
/// <see cref="Properties.PropertiesPanel"/> is this dialog's only intended host, mounting it only
/// while it is showing, the same "a fresh instance every open" pattern <c>MoveToPositionDialog</c>'s
/// own remarks explain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cycle rejection is the one thing that does not close this dialog (PRD §5, §13).</b>
/// <see cref="ApplyAsync"/> builds the candidate <see cref="CalcExpression"/> from this dialog's own
/// working state and calls
/// <see cref="ExpressionDependencyAnalysis.WouldCreateCalculationCycle"/> against it before ever
/// touching <see cref="EditContext"/>. A cycle leaves <see cref="DesignerEditContext.Draft"/>
/// entirely untouched and instead renders a <c>role="alert"</c> naming the cycle as an arrow-joined
/// chain of each node's own label, moving real DOM focus there so a screen-reader user hears the
/// rejection immediately — exactly <see cref="VisibilityRuleEditor"/>'s own contract, against the
/// independent calculation graph rather than the visibility one. Only a candidate that clears that
/// check — or an author clearing every operand, which can never introduce a cycle — ever reaches
/// <see cref="DesignerEditContext.UpdateNode"/>, and only then does this dialog raise
/// <see cref="OnClosed"/>.
/// </para>
/// <para>
/// <b>One commit, on Apply, never per control (AGENTS.md render discipline).</b> Every edit here —
/// the operation select, the format select, every <see cref="CalcOperandRow"/>'s own kind/field/
/// number, add, remove — only ever mutates this dialog's own working state; nothing reaches
/// <see cref="EditContext"/> until <see cref="ApplyAsync"/> runs.
/// </para>
/// <para>
/// <b>Operand field candidates (PRD §13).</b> <see cref="FieldsForOperand"/> offers each
/// <see cref="CalcOperandRow"/> the fields whose own answer shape matches the current
/// <see cref="_operation"/>'s own operand typing — number/currency/calc-of-number fields for the
/// four numeric operations, date/calc-of-date fields for the two date operations — unioned with the
/// row's own current field only to guard against a dangling reference (a field id an operand names
/// that the candidate list would otherwise silently drop with no explanation); it is never a way to
/// keep a genuinely type-mismatched field selected.
/// </para>
/// <para>
/// <b>An operation-category flip clears every field operand (PRD §13).</b>
/// <see cref="OnOperationChanged"/> — not a bare assignment — is what the Operation select commits
/// through: switching between the numeric operations (Sum/Subtract/Multiply/Divide) and the date
/// ones (DateAddDays/DateDiffDays) means every existing field-kind operand was only ever offered
/// as a candidate under the OLD category, and the two candidate sets are disjoint by construction
/// (<see cref="IsNumericField"/>/<see cref="IsDateField"/>), so it can never still be a legitimate
/// answer under the new one. Clearing each one's own <see cref="CalcOperand.Field"/> — never
/// silently keeping it "selected" behind the scenes — is what makes
/// <see cref="CalcOperandRow"/>'s own Field select actually show its unselected placeholder, so the
/// author sees the mismatch and must re-pick rather than Apply a calculation that can only ever
/// evaluate to no value. Switching between two operations that share a category (Sum to Multiply,
/// say) touches nothing, since every existing selection is still exactly as valid as it was.
/// </para>
/// </remarks>
public partial class CalculationEditor : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The static web asset path this component imports its focus-trap JS module from. See
    /// <c>MoveToPositionDialog.ModulePath</c>'s own remarks for the convention;
    /// <see langword="internal"/> so a test can set up the module mock against the exact path this
    /// component requests.
    /// </summary>
    internal const string ModulePath = "./_content/BlazeForms.Designer/Rules/CalculationEditor.razor.js";

    private static readonly CalcOperation[] AllOperations = Enum.GetValues<CalcOperation>();
    private static readonly CalcFormat[] AllFormats = Enum.GetValues<CalcFormat>();

    private readonly string _instanceId = "bf-calc-dialog-" + Guid.NewGuid().ToString("n");
    private ElementReference _dialogElement;
    private ElementReference _firstFocusElement;
    private ElementReference _alertElement;
    private ElementReference _addButtonElement;
    private IJSObjectReference? _module;
    private IJSObjectReference? _focusTrapHandle;
    private List<CalcOperand> _operands = [];
    private CalcOperation _operation;
    private CalcFormat _format;
    private IReadOnlyList<string>? _cyclePath;
    private bool _focusAlertOnNextRender;
    private int? _focusOperandIndex;
    private bool _focusAddButtonOnNextRender;
    private bool _disposed;

    /// <summary>
    /// The mutation engine this dialog applies its calculation through.
    /// </summary>
    [Parameter, EditorRequired]
    public DesignerEditContext EditContext { get; set; } = default!;

    /// <summary>
    /// The node whose <see cref="FormNode.Calculation"/> is being edited. Its current calculation
    /// (or the absence of one) seeds this dialog's own working state.
    /// </summary>
    [Parameter, EditorRequired]
    public string NodeId { get; set; } = default!;

    /// <summary>
    /// Raised once this dialog should close — after a successful apply (including one that clears
    /// the calculation) or a cancel. Never raised after a cycle rejection, since that leaves the
    /// dialog open for the author to fix.
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

    private string OperationSelectId => _instanceId + "-operation";

    private string FormatSelectId => _instanceId + "-format";

    private string NodeLabel => FieldLabelFor(RequireNode());

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        var node = RequireNode();
        _operation = node.Calculation?.Operation ?? CalcOperation.Sum;
        _format = node.Calculation?.Format ?? CalcFormat.Number;
        _operands = [.. node.Calculation?.Operands ?? []];
    }

    /// <summary>
    /// Clears <see cref="_focusOperandIndex"/> the moment this render has gone out — the same
    /// one-shot reset <see cref="VisibilityRuleEditor.OnAfterRender"/> uses for its own row focus
    /// signal.
    /// </summary>
    protected override void OnAfterRender(bool firstRender) => _focusOperandIndex = null;

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
    /// solely so a test can prove <see cref="DisposeAsync"/> actually disposes it.
    /// </summary>
    internal bool HasImportedModule => _module is not null;

    /// <summary>
    /// The cycle path a rejected <see cref="ApplyAsync"/> most recently reported, resolved to node
    /// labels and arrow-joined. <c>internal</c> purely so a test can assert on it directly rather
    /// than re-parsing the rendered alert's text.
    /// </summary>
    internal string? CycleErrorText => _cyclePath is { Count: > 0 } path ? CyclePathText(path) : null;

    /// <summary>
    /// Appends a new operand, defaulting to the first candidate field for the current operation
    /// (or a fully blank field-kind operand when none exist), and asks its own row's Kind select
    /// to take focus once this render lands (WCAG 2.4.3).
    /// </summary>
    private void AddOperand()
    {
        var candidates = CandidateFieldsFor(_operation);
        _operands.Add(new CalcOperand { Field = candidates.Count > 0 ? candidates[0].Id : null });
        _focusOperandIndex = _operands.Count - 1;
    }

    /// <summary>
    /// Removes the operand at <paramref name="index"/> and moves focus to a stable neighbour (PRD
    /// §11, WCAG 2.4.3) — the same rule <see cref="VisibilityRuleEditor.RemoveCondition"/> follows.
    /// </summary>
    private void RemoveOperand(int index)
    {
        _operands.RemoveAt(index);

        if (_operands.Count > 0)
        {
            _focusOperandIndex = Math.Max(0, index - 1);
        }
        else
        {
            _focusAddButtonOnNextRender = true;
        }
    }

    private void UpdateOperand(int index, CalcOperand operand) => _operands[index] = operand;

    /// <summary>
    /// Commits the Operation select. See this type's own remarks: only when
    /// <paramref name="operation"/>'s own operand-typing category (numeric vs. date) actually
    /// differs from <see cref="_operation"/>'s current one does this clear every field-kind
    /// operand's own <see cref="CalcOperand.Field"/> — a stale reference the new category's own
    /// candidate list would never have offered again anyway, surfaced rather than silently kept
    /// (code review fix #2).
    /// </summary>
    private void OnOperationChanged(CalcOperation operation)
    {
        var categoryChanged = IsDateOperation(_operation) != IsDateOperation(operation);
        _operation = operation;

        if (!categoryChanged)
        {
            return;
        }

        for (var i = 0; i < _operands.Count; i++)
        {
            if (_operands[i].Field is not null)
            {
                _operands[i] = _operands[i] with { Field = null };
            }
        }
    }

    /// <summary>
    /// Builds the candidate expression from this dialog's own working state and either applies it,
    /// clears the calculation (no operands left), or rejects it with the named cycle — see this
    /// type's own remarks.
    /// </summary>
    private Task ApplyAsync()
    {
        _cyclePath = null;
        var node = RequireNode();

        if (_operands.Count == 0)
        {
            EditContext.UpdateNode(node with { Calculation = null });
            return OnClosed.InvokeAsync();
        }

        var candidate = new CalcExpression { Operation = _operation, Operands = _operands, Format = _format };

        if (ExpressionDependencyAnalysis.WouldCreateCalculationCycle(EditContext.Draft.Definition, NodeId, candidate, out var cyclePath))
        {
            _cyclePath = cyclePath;
            _focusAlertOnNextRender = true;
            return Task.CompletedTask;
        }

        EditContext.UpdateNode(node with { Calculation = candidate });
        return OnClosed.InvokeAsync();
    }

    /// <summary>
    /// Cancels — the Esc path, and the visible Cancel button's — without touching
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

    /// <summary>
    /// The candidate fields for <paramref name="operand"/>'s own row: whichever fields
    /// <see cref="CandidateFieldsFor"/> offers for <see cref="_operation"/>, unioned with the
    /// operand's own current field when it is not already one of them (see this type's own
    /// remarks).
    /// </summary>
    private IReadOnlyList<FormNode> FieldsForOperand(CalcOperand operand)
    {
        var candidates = CandidateFieldsFor(_operation);

        if (operand.Field is not { } fieldId || candidates.Any(field => string.Equals(field.Id, fieldId, StringComparison.Ordinal)))
        {
            return candidates;
        }

        var current = EditContext.Draft.Definition.FindNode(fieldId);
        return current is null ? candidates : [.. candidates, current];
    }

    /// <summary>
    /// Every input node whose own answer shape matches <paramref name="operation"/>'s own operand
    /// typing (PRD §13): number/currency/calc-of-number fields for the four numeric operations,
    /// date/calc-of-date fields for the two date operations -- further narrowed to
    /// <see cref="NodeId"/>'s own repeating-group boundary (repeating-groups-plan.md, Increment
    /// C): a calc inside a group offers its own siblings plus every top-level field; a top-level
    /// calc excludes every group's children, matching the linter's own blocking FR-04 rule.
    /// </summary>
    private IReadOnlyList<FormNode> CandidateFieldsFor(CalcOperation operation)
    {
        var wantsDateFields = IsDateOperation(operation);

        var typedCandidates = EditContext.Draft.Definition.EnumerateNodes()
            .Where(node => FormSchema.IsInputNode(node.Type))
            .Where(node => wantsDateFields ? IsDateField(node) : IsNumericField(node));

        return RuleFieldBoundary.Filter(EditContext.Draft.Definition, NodeId, typedCandidates);
    }

    private static bool IsDateOperation(CalcOperation operation) =>
        operation is CalcOperation.DateAddDays or CalcOperation.DateDiffDays;

    private static bool IsNumericField(FormNode node) =>
        node.Type is NodeType.Number or NodeType.Currency
        || (node.Type == NodeType.Calc && node.Calculation is { Format: CalcFormat.Number or CalcFormat.Integer or CalcFormat.Currency });

    private static bool IsDateField(FormNode node) =>
        node.Type == NodeType.Date
        || (node.Type == NodeType.Calc && node.Calculation is { Format: CalcFormat.Date });

    private static string FieldLabelFor(FormNode node) =>
        node.Label ?? Localizer["UntitledNodeLabel", Localizer[$"NodeType{node.Type}"].Value].Value;
}
