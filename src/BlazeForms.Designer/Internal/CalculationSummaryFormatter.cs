using System.Globalization;
using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Designer.Internal;

/// <summary>
/// Builds the plain-text calculation summary <see cref="Properties.PropertiesPanel"/>'s calc group
/// shows for a <see cref="NodeType.Calc"/> node's own <see cref="FormNode.Calculation"/> (PRD §5,
/// §13) — the calc sibling of <see cref="VisibilitySummaryFormatter"/>. Resolves every field an
/// operand names to that field's own label (or its localized "Untitled {type}" fallback,
/// AGENTS.md invariant #5's stable-value rule governs storage, not display), a literal number to
/// its own culture-formatted text, and <see cref="CalcFunction.Today"/> to its own localized
/// phrase.
/// </summary>
internal static class CalculationSummaryFormatter
{
    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    /// <summary>
    /// Describes <paramref name="node"/>'s own <see cref="FormNode.Calculation"/> in plain
    /// language.
    /// </summary>
    /// <param name="node">
    /// The node whose calculation is being described.
    /// </param>
    /// <param name="definition">
    /// The definition <paramref name="node"/> belongs to, used only to resolve the labels of the
    /// fields the calculation's own operands name.
    /// </param>
    /// <returns>
    /// A single sentence, e.g. "Sum of Fee, 50." — or the localized "No calculation set." once
    /// <see cref="FormNode.Calculation"/> is <see langword="null"/>.
    /// </returns>
    internal static string Format(FormNode node, FormDefinition definition)
    {
        if (node.Calculation is not { } calculation)
        {
            return Localizer["CalcSummaryEmpty"].Value;
        }

        var operationPhrase = Localizer[$"CalcSummaryOperation{calculation.Operation}"].Value;
        var operandDescriptions = calculation.Operands.Select(operand => DescribeOperand(operand, definition));

        return Localizer["CalcSummaryFormat", operationPhrase, string.Join(", ", operandDescriptions)].Value;
    }

    private static string DescribeOperand(CalcOperand operand, FormDefinition definition) => operand switch
    {
        { Field: { } fieldId, Number: null, Function: null } => FieldLabel(fieldId, definition),
        { Number: { } number, Field: null, Function: null } => number.ToString(CultureInfo.CurrentCulture),
        { Function: CalcFunction.Today, Field: null, Number: null } => Localizer["CalcSummaryOperandToday"].Value,
        _ => Localizer["CalcSummaryOperandBlank"].Value,
    };

    private static string FieldLabel(string fieldId, FormDefinition definition)
    {
        var field = definition.FindNode(fieldId);
        return field is null
            ? fieldId
            : field.Label ?? Localizer["UntitledNodeLabel", Localizer[$"NodeType{field.Type}"].Value].Value;
    }
}
