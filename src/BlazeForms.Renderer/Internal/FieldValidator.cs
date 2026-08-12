using System.Globalization;
using BlazeForms.Definitions;
using BlazeForms.Resources;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Internal;

/// <summary>
/// Per-field validation: required, numeric bounds, and format, for one node against one answer
/// (PRD §4.2, §6). Stateless — <see cref="FormRenderer"/> keeps a single instance for the whole
/// fill and reuses it on blur, on page-advance, and on submit.
/// </summary>
/// <remarks>
/// A hidden node or a <see cref="NodeType.Calc"/> node is never validated (PRD §6): a caller that
/// has already excluded them from the set it checks never reaches this type for them, but
/// <see cref="Validate"/> also short-circuits on both cases directly, so a defensive caller gets
/// the same answer either way. Every message comes from a remedy-worded resx template that quotes
/// <see cref="FormNode.Label"/> — never a bare "this field is invalid" — resolved through
/// <see cref="IStringLocalizer{T}"/> so a host can localize without touching Core or Renderer
/// source (PRD §12).
/// </remarks>
internal sealed class FieldValidator
{
    private readonly IStringLocalizer<RendererStrings> _localizer;

    /// <summary>
    /// Creates a validator that resolves its remedy messages through <paramref name="localizer"/>.
    /// </summary>
    /// <param name="localizer">
    /// The localizer every remedy message is resolved through.
    /// </param>
    public FieldValidator(IStringLocalizer<RendererStrings> localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;
    }

    /// <summary>
    /// Validates one node's current answer.
    /// </summary>
    /// <param name="node">
    /// The node to validate. Never produces a message for <see cref="NodeType.Calc"/> or a
    /// static-content node.
    /// </param>
    /// <param name="value">
    /// The respondent's current answer, in the CLR shape
    /// <c>Fields/Internal/FieldValueConventions.cs</c> documents for <paramref name="node"/>'s
    /// type.
    /// </param>
    /// <param name="isVisible">
    /// Whether <paramref name="node"/> is currently visible. Governs whether
    /// <see cref="FormNode.RequiredWhenVisible"/> applies — a caller must never pass
    /// <see langword="true"/> for a node it knows to be hidden, since a hidden field is excluded
    /// from validation altogether (PRD §6).
    /// </param>
    /// <returns>
    /// The remedy-worded validation message, or <see langword="null"/> when the answer is valid.
    /// </returns>
    public string? Validate(FormNode node, object? value, bool isVisible)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Type == NodeType.Calc || !FormSchema.IsInputNode(node.Type))
        {
            return null;
        }

        var label = node.Label ?? "";
        var isRequired = node.Required || (node.RequiredWhenVisible && isVisible);

        return node.Type switch
        {
            NodeType.Text or NodeType.TextArea =>
                IsBlank(value as string) && isRequired ? Message("RequiredText", label) : null,
            NodeType.Email => ValidateEmail(value as string, isRequired, label),
            NodeType.Phone => ValidatePhone(value as string, isRequired, label),
            NodeType.Number => ValidateNumeric(value as decimal?, node, isRequired, label, "RequiredNumber"),
            NodeType.Currency => ValidateNumeric(value as decimal?, node, isRequired, label, "RequiredCurrency"),
            NodeType.Date =>
                value as DateOnly? is null && isRequired ? Message("RequiredDate", label) : null,
            NodeType.DateRange => ValidateDateRange(value, isRequired, label),
            NodeType.Select or NodeType.Radio or NodeType.YesNo =>
                IsBlank(value as string) && isRequired ? Message("RequiredChoice", label) : null,
            NodeType.CheckboxGroup =>
                isRequired && (value as IReadOnlyList<string> ?? []).Count == 0
                    ? Message("RequiredCheckboxGroup", label)
                    : null,
            NodeType.Boolean => isRequired && value is not true ? Message("RequiredBoolean", label) : null,
            _ => null,
        };
    }

    private string? ValidateEmail(string? value, bool isRequired, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return isRequired ? Message("RequiredEmail", label) : null;
        }

        return IsPlausibleEmail(value) ? null : Message("InvalidEmail", label);
    }

    private string? ValidatePhone(string? value, bool isRequired, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return isRequired ? Message("RequiredPhone", label) : null;
        }

        return IsPlausiblePhone(value) ? null : Message("InvalidPhone", label);
    }

    private string? ValidateNumeric(decimal? value, FormNode node, bool isRequired, string label, string requiredKey)
    {
        if (value is null)
        {
            return isRequired ? Message(requiredKey, label) : null;
        }

        if (node.Min is decimal min && value < min)
        {
            return Message("BelowMinimum", min.ToString(CultureInfo.CurrentCulture), label);
        }

        if (node.Max is decimal max && value > max)
        {
            return Message("AboveMaximum", max.ToString(CultureInfo.CurrentCulture), label);
        }

        return null;
    }

    private string? ValidateDateRange(object? value, bool isRequired, string label)
    {
        var parts = value as IReadOnlyList<string>;
        var hasStart = parts is { Count: > 0 } && !string.IsNullOrEmpty(parts[0]);
        var hasEnd = parts is { Count: > 1 } && !string.IsNullOrEmpty(parts[1]);

        return isRequired && !(hasStart && hasEnd) ? Message("RequiredDateRange", label) : null;
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// A deliberately loose email shape check — an <c>@</c> not at either end, followed later by
    /// a <c>.</c> that is itself not at either end, with no embedded space. It exists to catch
    /// obvious typos ("respondent@", "respondent example.com"), not to fully validate RFC 5321
    /// syntax, which rejects real addresses far more often than it catches typos.
    /// </summary>
    private static bool IsPlausibleEmail(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);

        if (at <= 0 || at == value.Length - 1)
        {
            return false;
        }

        var dotInDomain = value[(at + 1)..].IndexOf('.', StringComparison.Ordinal);
        var dot = dotInDomain < 0 ? -1 : dotInDomain + at + 1;

        return dot > at + 1 && dot < value.Length - 1 && !value.Contains(' ', StringComparison.Ordinal);
    }

    /// <summary>
    /// A deliberately loose phone shape check: 7 to 15 digits, allowing the punctuation a
    /// respondent commonly types (spaces, a leading <c>+</c>, hyphens, parentheses, dots)
    /// anywhere else. It exists to catch obvious typos, not to validate a specific national
    /// numbering plan.
    /// </summary>
    private static bool IsPlausiblePhone(string value)
    {
        var digitCount = 0;

        foreach (var character in value)
        {
            if (char.IsDigit(character))
            {
                digitCount++;
            }
            else if (character is not (' ' or '+' or '-' or '(' or ')' or '.'))
            {
                return false;
            }
        }

        return digitCount is >= 7 and <= 15;
    }

    private string Message(string key, params object[] arguments) => _localizer[key, arguments].Value;
}
