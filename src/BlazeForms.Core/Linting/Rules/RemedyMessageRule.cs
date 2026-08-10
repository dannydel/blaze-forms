using BlazeForms.Definitions;

namespace BlazeForms.Linting.Rules;

/// <summary>
/// A11Y-06 (Advisory): a validation message should state the remedy — "Enter a date for 'Date of
/// birth'." — not merely name the failure — "Invalid" (PRD §6, §8). The check is a conservative
/// heuristic: it flags a message only when it is a bare non-actionable word, is very short, or
/// contains no remedy verb, so a genuine instruction is left alone.
/// </summary>
internal sealed class RemedyMessageRule : ILintRule
{
    private static readonly string[] NonActionableWords = ["required", "invalid", "error", "wrong"];

    private static readonly string[] RemedyVerbs =
    [
        "enter", "choose", "select", "provide", "add", "use", "pick", "fill", "type", "answer",
        "complete", "specify", "include", "upload", "confirm", "check", "review", "correct",
        "update", "set", "give", "make", "ensure",
    ];

    private static readonly char[] WordTrim = [',', '.', ';', ':', '!', '?', '\'', '"', '(', ')'];

    /// <inheritdoc />
    public string Id => LintRuleIds.A11y06;

    /// <inheritdoc />
    public LintSeverity Severity => LintSeverity.Advisory;

    /// <inheritdoc />
    public string Rationale =>
        "A message that names only the failure leaves the respondent guessing; stating the remedy tells them exactly what to do next.";

    /// <inheritdoc />
    public IEnumerable<LintResult> Analyze(LintContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<LintResult>();

        foreach (var rule in context.Definition.ValidationRules)
        {
            if (!StatesNoRemedy(rule.Message))
            {
                continue;
            }

            results.Add(new LintResult
            {
                RuleId = Id,
                Severity = Severity,
                Message = "This validation message does not state a remedy.",
                Detail = $"Rephrase \"{rule.Message}\" to tell the respondent what to do.",
                NodeId = rule.Target,
            });
        }

        return results;
    }

    private static bool StatesNoRemedy(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return true;
        }

        var normalized = message.Trim().Trim(WordTrim).Trim();

        if (NonActionableWords.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var words = normalized
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim(WordTrim))
            .Where(word => word.Length > 0)
            .ToArray();

        // A one- or two-word message ("Invalid input", "Required field") has no room to instruct.
        if (words.Length <= 2)
        {
            return true;
        }

        // With no remedy verb anywhere, the message describes the failure rather than the fix.
        return !words.Any(word => RemedyVerbs.Contains(word, StringComparer.OrdinalIgnoreCase));
    }
}
