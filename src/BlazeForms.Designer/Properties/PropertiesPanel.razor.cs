using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BlazeForms.Definitions;
using BlazeForms.Designer;
using BlazeForms.Designer.Internal;
using BlazeForms.Internal;
using BlazeForms.Resources;
using BlazeForms.Rules;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Properties;

/// <summary>
/// The docked properties pane (PRD §4.1): every field-specific control for whichever node
/// <see cref="DesignerEditContext.Selection"/> currently names, dispatched by
/// <see cref="FormNode.Type"/>, with an accessible empty state when nothing is selected.
/// </summary>
/// <remarks>
/// <para>
/// <b>One commit per edit, never per keystroke.</b> Every control here binds on
/// <c>onchange</c> — Blazor's default binding event, i.e. blur, or an immediate change for a
/// checkbox or <c>&lt;select&gt;</c> — never <c>oninput</c>, so a single edit produces exactly one
/// <see cref="DesignerEditContext.UpdateNode"/> call, i.e. exactly one undo entry and one autosave
/// (PRD §4.1's depth-50 undo stack would otherwise flood on every character typed into a help
/// text). This also means typing in a property never touches
/// <see cref="Canvas.DesignerCanvas"/>'s own row markup until the edit actually commits on blur —
/// AGENTS.md's render-discipline standard, satisfied here for free by never mutating anything
/// before that point.
/// </para>
/// <para>
/// <b>Type dispatch.</b> <see cref="FormSchema.IsInputNode"/> gates the input-only group (help,
/// placeholder, required, required-while-visible, half-width); <see cref="FormNode.Type"/> itself
/// gates the numeric bounds (<c>number</c>/<c>currency</c>), the heading level select, the choice
/// types' <see cref="OptionsEditor"/>, and the static-prose content textarea
/// (<c>paragraph</c>/<c>callout</c>) — exactly PRD §5's node-type table. The field ID and label
/// controls, and the visibility-rule summary, show for every node type.
/// </para>
/// <para>
/// <b>Focus coordination.</b> <see cref="OnEditContextStateChanged"/> only claims focus for this
/// panel's own label input when the new selection both names a node that is different from the
/// one this panel last focused <em>and</em> carries
/// <see cref="DesignerFocusIntent.None"/> — the intent
/// <see cref="Canvas.DesignerCanvas"/>'s own click/Enter commit path
/// (<c>DesignerCanvas.Activate</c>) always uses, and deliberately the only intent
/// <see cref="Canvas.DesignerCanvas"/> itself never moves DOM focus for (see its own remarks). A
/// structural mutation (add, delete, move, duplicate, undo, redo) tags every other
/// <see cref="DesignerFocusIntent"/> value precisely because focus belongs back on the canvas row
/// it affected, not in this panel — so this panel steps aside for those. The one-shot
/// <c>_focusLabelOnNextRender</c> flag and <see cref="ElementReferenceExtensions.FocusAsync(ElementReference)"/>
/// call in <see cref="OnAfterRenderAsync"/> mirror the same pattern
/// <see cref="Canvas.CanvasNodeRow"/> uses for its own post-mutation focus moves.
/// </para>
/// <para>
/// <b>Markdown safety (AGENTS.md invariant #6).</b> Every control here is a plain
/// <c>&lt;input&gt;</c>/<c>&lt;textarea&gt;</c> editing a raw string — help and content text are
/// never rendered as <see cref="MarkupString"/> in this editor. The "Supports Markdown" hint next
/// to help and content is only ever a label, never a render of the author's own text.
/// </para>
/// </remarks>
public partial class PropertiesPanel : ComponentBase, IAsyncDisposable
{
    private DesignerEditContext? _subscribedContext;
    private string? _lastFocusedNodeId;
    private bool _focusLabelOnNextRender;
    private bool _focusVisibilityActionOnNextRender;
    private bool _focusCalculationActionOnNextRender;
    private ElementReference _labelElement;
    private ElementReference _visibilityActionElement;
    private ElementReference _calculationActionElement;
    private string? _visibilityRuleEditorNodeId;
    private string? _calculationEditorNodeId;
    private bool _disposed;

    /// <summary>
    /// The mutation engine this panel reads its selection and content from, and routes every
    /// commit through via <see cref="DesignerEditContext.UpdateNode"/>.
    /// </summary>
    [Parameter, EditorRequired]
    public DesignerEditContext EditContext { get; set; } = default!;

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private FormNode? SelectedNode => EditContext.Selection.NodeId is { } nodeId
        ? EditContext.Draft.Definition.FindNode(nodeId)
        : null;

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        // Seeds the baseline OnEditContextStateChanged compares against from whatever selection
        // is already current at mount, so a panel that mounts onto an already-selected node (a
        // host that restores a prior selection, or a test that pre-selects before rendering)
        // never steals focus purely from having just appeared -- only an actual, subsequent
        // change moves focus, mirroring CanvasNodeRow's own "seed before ShouldRender is ever
        // asked" rationale.
        _lastFocusedNodeId = EditContext.Selection.NodeId;
    }

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
    }

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusLabelOnNextRender)
        {
            _focusLabelOnNextRender = false;
            await _labelElement.FocusAsync();
        }

        if (_focusVisibilityActionOnNextRender)
        {
            _focusVisibilityActionOnNextRender = false;
            await _visibilityActionElement.FocusAsync();
        }

        if (_focusCalculationActionOnNextRender)
        {
            _focusCalculationActionOnNextRender = false;
            await _calculationActionElement.FocusAsync();
        }
    }

    /// <summary>
    /// Unsubscribes from <see cref="EditContext"/>. Safe to call more than once; never disposes
    /// <see cref="EditContext"/> itself -- that remains its owner's (<see cref="FormDesigner"/>'s)
    /// job, the same split <see cref="AriaLiveRegion"/> and <see cref="Canvas.DesignerCanvas"/>
    /// both observe.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        if (_subscribedContext is not null)
        {
            _subscribedContext.StateChanged -= OnEditContextStateChanged;
            _subscribedContext = null;
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// See this type's own remarks on focus coordination for the intent this checks.
    /// </summary>
    private void OnEditContextStateChanged() => InvokeAsync(() =>
    {
        var selection = EditContext.Selection;

        if (selection.Intent == DesignerFocusIntent.None
            && selection.NodeId is { } nodeId
            && !string.Equals(_lastFocusedNodeId, nodeId, StringComparison.Ordinal))
        {
            _lastFocusedNodeId = nodeId;
            _focusLabelOnNextRender = true;
        }
        else
        {
            _lastFocusedNodeId = selection.NodeId;
        }

        StateHasChanged();
    });

    private static string IdFor(FormNode node, string suffix) =>
        string.Concat("bf-props-", node.Id, "-", suffix);

    /// <summary>
    /// Whether the empty-label affordance should show for <paramref name="node"/> — aligned with
    /// the linter's blocking A11Y-01 rule (<c>A11yLabelRule</c>), which only ever flags an input
    /// node with no label; a static content node's own label (a heading's text, for instance) is
    /// not this rule's concern, so this panel does not warn about it either.
    /// </summary>
    private static bool ShowEmptyLabelWarning(FormNode node) =>
        FormSchema.IsInputNode(node.Type) && string.IsNullOrWhiteSpace(node.Label);

    private void CommitLabel(FormNode node, ChangeEventArgs e) =>
        EditContext.UpdateNode(node with { Label = NormalizeToNull(e.Value) });

    // Help and Content bind on the textarea's DOM `value` property (@bind:get/@bind:set) rather
    // than a plain @onchange over ChangeEventArgs, the same reason TextAreaField.razor
    // (BlazeForms.Renderer) does: a <textarea>'s value lives only in that DOM property once a
    // user has touched it, never in its rendered child content, so only @bind's special
    // textarea-value handling keeps this panel's own re-renders (e.g. an undo restoring Help)
    // visible in the browser. The default bind event stays "onchange" -- i.e. blur -- exactly the
    // one-commit-per-edit discipline every other control here follows.
    private Task CommitHelpAsync(FormNode node, string? value)
    {
        EditContext.UpdateNode(node with { Help = NormalizeToNull(value) });
        return Task.CompletedTask;
    }

    private Task CommitContentAsync(FormNode node, string? value)
    {
        EditContext.UpdateNode(node with { Content = NormalizeToNull(value) });
        return Task.CompletedTask;
    }

    private void CommitPlaceholder(FormNode node, ChangeEventArgs e) =>
        EditContext.UpdateNode(node with { Placeholder = NormalizeToNull(e.Value) });

    private void CommitRequired(FormNode node, ChangeEventArgs e) =>
        EditContext.UpdateNode(node with { Required = ToBool(e.Value) });

    private void CommitRequiredWhenVisible(FormNode node, ChangeEventArgs e) =>
        EditContext.UpdateNode(node with { RequiredWhenVisible = ToBool(e.Value) });

    private void CommitHalf(FormNode node, ChangeEventArgs e) =>
        EditContext.UpdateNode(node with { Half = ToBool(e.Value) });

    private void CommitMin(FormNode node, ChangeEventArgs e) =>
        EditContext.UpdateNode(node with { Min = ParseDecimal(e.Value) });

    private void CommitMax(FormNode node, ChangeEventArgs e) =>
        EditContext.UpdateNode(node with { Max = ParseDecimal(e.Value) });

    private Task CommitLevelAsync(FormNode node, int value)
    {
        EditContext.UpdateNode(node with { Level = value is >= 2 and <= 4 ? value : 2 });
        return Task.CompletedTask;
    }

    private void CommitOptions(FormNode node, IReadOnlyList<FormOption> options) =>
        EditContext.UpdateNode(node with { Options = options });

    /// <summary>
    /// Opens <see cref="VisibilityRuleEditor"/> for <paramref name="nodeId"/> -- the Add and Edit
    /// rule buttons' shared path (PRD §4.1, §6). A fresh instance mounts for every open, the same
    /// "a fresh instance every open" pattern <c>MoveToPositionDialog</c>'s own remarks explain, so
    /// its own working state always starts from whatever rule (or absence of one) is current when
    /// the button is pressed.
    /// </summary>
    private void OpenVisibilityRuleEditor(string nodeId) => _visibilityRuleEditorNodeId = nodeId;

    /// <summary>
    /// Closes <see cref="VisibilityRuleEditor"/> -- after a successful apply or a cancel -- and
    /// re-requests focus for whichever visibility action button now renders (Add once the rule was
    /// cleared or never existed, Edit once one does), mirroring
    /// <c>Canvas.DesignerCanvas.CloseMoveDialog</c>'s own post-close focus return.
    /// </summary>
    private void CloseVisibilityRuleEditor()
    {
        _visibilityRuleEditorNodeId = null;
        _focusVisibilityActionOnNextRender = true;
    }

    /// <summary>
    /// Clears a node's own visibility rule directly -- the Remove button's path. Never opens
    /// <see cref="VisibilityRuleEditor"/>: removing a rule can never introduce the cycle that
    /// dialog's own Apply step guards against, so there is nothing here for it to gate.
    /// </summary>
    private void RemoveVisibilityRule(FormNode node) => EditContext.UpdateNode(node with { VisibleWhen = null });

    /// <summary>
    /// Opens <see cref="CalculationEditor"/> for <paramref name="nodeId"/> — the Add and Edit
    /// calculation buttons' shared path (PRD §4.1, §5, §13), mirroring
    /// <see cref="OpenVisibilityRuleEditor"/>'s own "a fresh instance every open" pattern.
    /// </summary>
    private void OpenCalculationEditor(string nodeId) => _calculationEditorNodeId = nodeId;

    /// <summary>
    /// Closes <see cref="CalculationEditor"/> — after a successful apply or a cancel — and
    /// re-requests focus for whichever calculation action button now renders, mirroring
    /// <see cref="CloseVisibilityRuleEditor"/>'s own post-close focus return.
    /// </summary>
    private void CloseCalculationEditor()
    {
        _calculationEditorNodeId = null;
        _focusCalculationActionOnNextRender = true;
    }

    /// <summary>
    /// Clears a node's own calculation directly — the Remove button's path. Never opens
    /// <see cref="CalculationEditor"/>: removing a calculation can never introduce the cycle that
    /// dialog's own Apply step guards against, so there is nothing here for it to gate.
    /// </summary>
    private void RemoveCalculation(FormNode node) => EditContext.UpdateNode(node with { Calculation = null });

    private static string? NormalizeToNull(object? value)
    {
        var text = value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool ToBool(object? value) => value is true;

    private static decimal? ParseDecimal(object? value) =>
        decimal.TryParse(value?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
