using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Designer.Internal;

/// <summary>
/// Builds the plain-text logic summary <see cref="Canvas.CanvasNodeRow"/>'s chip and
/// <see cref="Properties.PropertiesPanel"/>'s visibility summary both show for a node's
/// <see cref="FormNode.VisibleWhen"/> (PRD §4.1: "a logic summary chip when a visibility rule
/// exists"). <see cref="Canvas.DesignerCanvas"/> calls this once per row, from the full definition
/// it already has in scope, and hands the leaf row nothing more than the resulting string -- the
/// row itself never needs the whole definition just to describe its own rule.
/// </summary>
internal static class VisibilitySummaryFormatter
{
    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private static readonly ConditionOperator[] NoOperandOperators =
    [
        ConditionOperator.IsTrue,
        ConditionOperator.IsFalse,
        ConditionOperator.IsBlank,
        ConditionOperator.IsNotBlank,
    ];

    /// <summary>
    /// Describes <paramref name="node"/>'s own <see cref="FormNode.VisibleWhen"/> in plain
    /// language, resolving every field it names to that field's own label (or its localized
    /// "Untitled {type}" fallback) and, for a choice field, its option's label rather than the
    /// raw stored value (AGENTS.md invariant #5's stable-value rule governs storage, not display).
    /// </summary>
    /// <param name="node">
    /// The node whose rule is being described. Its own <see cref="FormNode.VisibleWhen"/> must not
    /// be <see langword="null"/> -- callers only ever ask for a summary once they already know a
    /// rule exists (the chip and the properties panel summary both gate on that first).
    /// </param>
    /// <param name="definition">
    /// The definition <paramref name="node"/> belongs to, used only to resolve the labels of the
    /// fields the rule names.
    /// </param>
    /// <returns>
    /// A single sentence: e.g. "Shown when Status is 'Active'." for one condition, or "Shown when
    /// all of 2 conditions." once there is more than one.
    /// </returns>
    internal static string Format(FormNode node, FormDefinition definition)
    {
        var rule = node.VisibleWhen!;

        if (rule.Conditions.Count == 0)
        {
            return rule.Join == ConditionJoin.All
                ? Localizer["LogicSummaryAlwaysShown"].Value
                : Localizer["LogicSummaryNeverShown"].Value;
        }

        if (rule.Conditions.Count == 1)
        {
            return DescribeCondition(rule.Conditions[0], definition);
        }

        var key = rule.Join == ConditionJoin.All ? "LogicSummaryAllOfN" : "LogicSummaryAnyOfN";
        return Localizer[key, rule.Conditions.Count].Value;
    }

    private static string DescribeCondition(Condition condition, FormDefinition definition)
    {
        var fieldLabel = FieldLabel(condition.Field, definition);
        var operatorPhrase = Localizer[$"ConditionOperator{condition.Operator}"].Value;

        return NoOperandOperators.Contains(condition.Operator) || condition.Value is null
            ? Localizer["LogicSummaryNoValue", fieldLabel, operatorPhrase].Value
            : Localizer["LogicSummaryWithValue", fieldLabel, operatorPhrase, ValueLabel(condition, definition)].Value;
    }

    private static string FieldLabel(string fieldId, FormDefinition definition)
    {
        var field = definition.FindNode(fieldId);
        return field is null
            ? fieldId
            : field.Label ?? Localizer["UntitledNodeLabel", Localizer[$"NodeType{field.Type}"].Value].Value;
    }

    private static string ValueLabel(Condition condition, FormDefinition definition)
    {
        var field = definition.FindNode(condition.Field);

        if (field is not null && IsChoiceType(field.Type))
        {
            var option = field.Options.FirstOrDefault(o => string.Equals(o.Value, condition.Value, StringComparison.Ordinal));

            if (option is not null)
            {
                return option.Label;
            }
        }

        return condition.Value ?? string.Empty;
    }

    private static bool IsChoiceType(NodeType type) =>
        type is NodeType.Select or NodeType.Radio or NodeType.CheckboxGroup or NodeType.YesNo;
}
