namespace BlazeForms.Linting;

/// <summary>
/// One finding from the linter (PRD §8): a rule ID, a severity, a plain-language message, and
/// enough location to drive the designer's jump-to-node action. Runtime only — never serialized.
/// </summary>
public sealed record LintResult
{
    /// <summary>
    /// The identifier of the rule that produced this result, from <see cref="LintRuleIds"/>.
    /// </summary>
    public required string RuleId { get; init; }

    /// <summary>
    /// Whether this result gates publishing.
    /// </summary>
    public required LintSeverity Severity { get; init; }

    /// <summary>
    /// The message shown to the author, in plain language.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Optional supporting detail — the offending value, the missing field's ID, or the specific
    /// levels involved.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// The node the result anchors to, if any. A rule sets this; the engine fills
    /// <see cref="PageIndex"/> and <see cref="SectionIndex"/> from it.
    /// </summary>
    public string? NodeId { get; init; }

    /// <summary>
    /// The zero-based page the anchored node lives on, filled by the engine. Null when the result
    /// has no node or the node is not in the definition.
    /// </summary>
    public int? PageIndex { get; init; }

    /// <summary>
    /// The zero-based section the anchored node lives in, filled by the engine. Null when the
    /// result has no node or the node is not in the definition.
    /// </summary>
    public int? SectionIndex { get; init; }
}
