using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Designer;
using BlazeForms.Designer.Internal;
using BlazeForms.Expressions;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Rules;

/// <summary>
/// Manages <see cref="FormDefinition.ValidationRules"/> wholesale (PRD §4.1, §6): each rule shows
/// its own Target (node select), Message (remedy-worded text -- advisory lint A11Y-06 otherwise,
/// which this editor does not itself enforce), and Expression, the latter built from the same
/// <see cref="ConditionRow"/>/All-Any-toggle UI <see cref="VisibilityRuleEditor"/> uses. Renders
/// inline rather than as a dialog -- a form's rule list has no natural upper bound the way a single
/// node's own visibility rule does, so a fixed-size modal would only ever get in its own way.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every action commits immediately (AGENTS.md render discipline).</b> Unlike
/// <see cref="VisibilityRuleEditor"/>'s Apply step, nothing here needs to hold a candidate back for
/// approval -- there is no cycle gate for cross-field validation rules in P1 (PRD §6 leaves that
/// non-blocking; this editor does not attempt one at all), so every add, remove, and field commit
/// (never a keystroke: every control here binds on blur or an immediate select/radio change, the
/// same one-commit-per-edit discipline <c>PropertiesPanel</c>/<c>OptionsEditor</c> already follow)
/// calls <see cref="DesignerEditContext.SetValidationRules"/> straight away with the complete
/// replacement list. That also means this editor holds no local draft state of its own: every
/// render reads <see cref="Rules"/> straight off <see cref="EditContext"/>.
/// </para>
/// <para>
/// <b>Row identity.</b> A <see cref="ValidationRule"/> carries no identifier of its own (unlike a
/// <see cref="FormNode"/>), so this editor keys each rule's own markup by its position in
/// <see cref="Rules"/> rather than a stable value the way <c>OptionsEditor</c> keys its own rows by
/// each option's <see cref="Definitions.FormOption.Value"/> -- inserting or removing a rule other
/// than the last one can therefore shift which DOM node backs a later rule's own controls. A
/// deliberate simplification for this first cut, not a correctness gap: no rule's own data is ever
/// lost or attributed to the wrong rule, since every commit always rebuilds and replaces the whole
/// list from <see cref="Rules"/>' own current content, never from a row's remembered position.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class ValidationRuleEditor : ComponentBase, IAsyncDisposable
{
    private readonly string _instanceId = "bf-validation-rules-" + Guid.NewGuid().ToString("n");
    private readonly List<RuleFocusRefs> _ruleRefs = [];
    private DesignerEditContext? _subscribedContext;
    private ElementReference _addRuleButtonElement;
    private (int RuleIndex, int ConditionIndex)? _focusConditionCoordinate;
    private int? _focusRuleTargetIndex;
    private int? _focusAddConditionButtonRuleIndex;
    private bool _focusAddRuleButtonOnNextRender;
    private bool _disposed;

    /// <summary>
    /// The mutation engine this editor reads <see cref="FormDefinition.ValidationRules"/> from and
    /// writes every change back through.
    /// </summary>
    [Parameter, EditorRequired]
    public DesignerEditContext EditContext { get; set; } = default!;

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private string HeadingId => _instanceId + "-heading";

    private IReadOnlyList<ValidationRule> Rules => EditContext.Draft.Definition.ValidationRules;

    private IReadOnlyList<FormNode> InputFields =>
        [.. EditContext.Draft.Definition.EnumerateNodes().Where(node => FormSchema.IsInputNode(node.Type))];

    /// <summary>
    /// The condition-row field candidates for <paramref name="rule"/> -- <see cref="InputFields"/>
    /// filtered to <paramref name="rule"/>'s own boundary (repeating-groups-plan.md, Increment C):
    /// a rule targeting a repeating group's own child may reference that child's siblings and
    /// every top-level field; a rule targeting a top-level field excludes every group's children.
    /// Matches the linter's own FR-04 rule exactly -- both key off
    /// <see cref="ExpressionDependencyAnalysis.GetRepeatingGroupOf"/> against
    /// <see cref="ValidationRule.Target"/>. <see cref="Rules.ConditionRow.Fields"/> is the picker
    /// this feeds; <see cref="InputFields"/> itself stays unfiltered for the Target select, since a
    /// rule's own target is not itself scoped by anything.
    /// </summary>
    private IReadOnlyList<FormNode> ConditionFieldsFor(ValidationRule rule) =>
        string.IsNullOrWhiteSpace(rule.Target)
            ? InputFields
            : RuleFieldBoundary.Filter(EditContext.Draft.Definition, rule.Target, InputFields);

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

    /// <summary>
    /// Clears <see cref="_focusConditionCoordinate"/> the moment this render has gone out --
    /// <see cref="ConditionRow"/>'s own <c>OnParametersSet</c> has already latched whatever
    /// <see cref="ConditionRow.RequestFocus"/> value this render handed it by the time this runs,
    /// so clearing here (rather than in <see cref="OnAfterRenderAsync"/>) is safe and stops the
    /// same coordinate from re-stealing focus on some later, unrelated render -- the same one-shot
    /// reset <c>Canvas.DesignerCanvas.OnAfterRender</c> uses for its own row focus signal. The
    /// remaining focus fields (<see cref="_focusRuleTargetIndex"/>,
    /// <see cref="_focusAddConditionButtonRuleIndex"/>, <see cref="_focusAddRuleButtonOnNextRender"/>)
    /// name elements this editor owns directly rather than a child component, so they are read and
    /// cleared together, inside <see cref="OnAfterRenderAsync"/> itself, immediately before the
    /// matching <see cref="ElementReferenceExtensions.FocusAsync(ElementReference)"/> call.
    /// </summary>
    protected override void OnAfterRender(bool firstRender) => _focusConditionCoordinate = null;

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusRuleTargetIndex is { } ruleIndex && ruleIndex < _ruleRefs.Count)
        {
            _focusRuleTargetIndex = null;
            await _ruleRefs[ruleIndex].TargetSelect.FocusAsync();
        }
        else if (_focusAddConditionButtonRuleIndex is { } addConditionRuleIndex && addConditionRuleIndex < _ruleRefs.Count)
        {
            _focusAddConditionButtonRuleIndex = null;
            await _ruleRefs[addConditionRuleIndex].AddConditionButton.FocusAsync();
        }
        else if (_focusAddRuleButtonOnNextRender)
        {
            _focusAddRuleButtonOnNextRender = false;
            await _addRuleButtonElement.FocusAsync();
        }
    }

    /// <summary>
    /// Unsubscribes from <see cref="EditContext"/>. Safe to call more than once; never disposes
    /// <see cref="EditContext"/> itself, the same split <c>PropertiesPanel</c> and
    /// <c>DesignerCanvas</c> both observe.
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

    private void OnEditContextStateChanged() => InvokeAsync(StateHasChanged);

    private string TargetId(int index) => _instanceId + "-" + index + "-target";

    private string MessageId(int index) => _instanceId + "-" + index + "-message";

    private string JoinGroupName(int index) => _instanceId + "-" + index + "-join";

    private static string FieldLabel(FormNode field) =>
        field.Label ?? Localizer["UntitledNodeLabel", Localizer[$"NodeType{field.Type}"].Value].Value;

    /// <summary>
    /// Appends a new rule, targeting the form's first input field by default, with an empty
    /// message and an empty (vacuously satisfied, so authoring starts from "never fires" rather
    /// than "always fires") expression, and asks its own Target select to take focus once this
    /// render lands (WCAG 2.4.3).
    /// </summary>
    private void AddRule()
    {
        var fallbackTarget = InputFields.Count > 0 ? InputFields[0].Id : string.Empty;
        var rule = new ValidationRule { Target = fallbackTarget, Message = string.Empty, Expression = new ConditionGroup() };
        _focusRuleTargetIndex = Rules.Count;
        Commit([.. Rules, rule]);
    }

    /// <summary>
    /// Removes the rule at <paramref name="index"/> and moves focus to a stable neighbour (PRD
    /// §11, WCAG 2.4.3): the previous rule's own Target select (or, removing the first rule,
    /// whichever rule now sits at index zero -- <see cref="Math.Max(int, int)"/> against
    /// <c>index - 1</c> covers both), or, once no rule survives at all, the add-rule button.
    /// </summary>
    private void RemoveRule(int index)
    {
        var rules = Rules.ToList();
        rules.RemoveAt(index);

        if (rules.Count > 0)
        {
            _focusRuleTargetIndex = Math.Max(0, index - 1);
        }
        else
        {
            _focusAddRuleButtonOnNextRender = true;
        }

        Commit(rules);
    }

    private Task CommitTargetAsync(int index, string? value)
    {
        ReplaceRule(index, Rules[index] with { Target = value ?? string.Empty });
        return Task.CompletedTask;
    }

    private Task CommitMessageAsync(int index, string? value)
    {
        ReplaceRule(index, Rules[index] with { Message = value ?? string.Empty });
        return Task.CompletedTask;
    }

    private void CommitJoin(int index, ConditionJoin join) =>
        ReplaceRule(index, Rules[index] with { Expression = Rules[index].Expression with { Join = join } });

    /// <summary>
    /// Appends a new condition to the rule at <paramref name="index"/> and asks its own row's
    /// Field select to take focus once this render lands (WCAG 2.4.3).
    /// </summary>
    private void AddCondition(int index)
    {
        var rule = Rules[index];
        var candidates = ConditionFieldsFor(rule);
        var fallbackField = candidates.Count > 0 ? candidates[0].Id : string.Empty;
        var condition = new Condition { Field = fallbackField, Operator = ConditionOperator.Is };
        _focusConditionCoordinate = (index, rule.Expression.Conditions.Count);
        ReplaceRule(index, rule with { Expression = rule.Expression with { Conditions = [.. rule.Expression.Conditions, condition] } });
    }

    /// <summary>
    /// Removes the condition at <paramref name="conditionIndex"/> from the rule at
    /// <paramref name="ruleIndex"/> and moves focus to a stable neighbour within that same rule
    /// (PRD §11, WCAG 2.4.3): the previous row's own Field select (or, removing the first row,
    /// whichever row now sits at index zero), or, once no condition survives in this rule at all,
    /// that rule's own add-condition button.
    /// </summary>
    private void RemoveCondition(int ruleIndex, int conditionIndex)
    {
        var rule = Rules[ruleIndex];
        var conditions = rule.Expression.Conditions.ToList();
        conditions.RemoveAt(conditionIndex);

        if (conditions.Count > 0)
        {
            _focusConditionCoordinate = (ruleIndex, Math.Max(0, conditionIndex - 1));
        }
        else
        {
            _focusAddConditionButtonRuleIndex = ruleIndex;
        }

        ReplaceRule(ruleIndex, rule with { Expression = rule.Expression with { Conditions = conditions } });
    }

    private void CommitCondition(int ruleIndex, int conditionIndex, Condition condition)
    {
        var rule = Rules[ruleIndex];
        var conditions = rule.Expression.Conditions.ToList();
        conditions[conditionIndex] = condition;
        ReplaceRule(ruleIndex, rule with { Expression = rule.Expression with { Conditions = conditions } });
    }

    private void ReplaceRule(int index, ValidationRule updated)
    {
        var rules = Rules.ToList();
        rules[index] = updated;
        Commit(rules);
    }

    private void Commit(IReadOnlyList<ValidationRule> rules) => EditContext.SetValidationRules(rules);

    /// <summary>
    /// Returns the same <see cref="RuleFocusRefs"/> instance for a given rule index on every call
    /// within a render, growing <see cref="_ruleRefs"/> as needed -- the markup's own
    /// <c>@ref</c> captures land on this instance's <see cref="RuleFocusRefs.TargetSelect"/> and
    /// <see cref="RuleFocusRefs.AddConditionButton"/> fields, the same reason
    /// <c>OptionsEditor.OptionRow.Input</c> captures its own row's element the same way. Rule
    /// identity here is positional, not stable (see this type's own remarks on row identity), so a
    /// removed rule's trailing entry is simply never read again rather than actively pruned --
    /// harmless, since it holds nothing but a couple of now-detached <see cref="ElementReference"/>
    /// structs.
    /// </summary>
    private RuleFocusRefs RuleRefsFor(int index)
    {
        while (_ruleRefs.Count <= index)
        {
            _ruleRefs.Add(new RuleFocusRefs());
        }

        return _ruleRefs[index];
    }

    /// <summary>
    /// One rule's own focus-relevant element captures -- see <see cref="RuleRefsFor"/>.
    /// </summary>
    private sealed class RuleFocusRefs
    {
        public ElementReference TargetSelect { get; set; }

        public ElementReference AddConditionButton { get; set; }
    }
}
