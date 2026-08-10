namespace BlazeForms.Linting;

/// <summary>
/// A single lint rule (PRD §8). Rules are pluggable: a host or contributor implements this
/// interface, ships an ID, a rationale, and tests, and registers the rule alongside the built-in
/// set. A rule reports only what it finds — the engine enriches each result with its page and
/// section — so an implementation sets <see cref="LintResult.NodeId"/> and leaves the indices
/// alone.
/// </summary>
public interface ILintRule
{
    /// <summary>
    /// The rule's identifier, stable and documented, from <see cref="LintRuleIds"/> for the
    /// built-ins.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The severity every result from this rule carries.
    /// </summary>
    LintSeverity Severity { get; }

    /// <summary>
    /// A one-sentence explanation of why the rule exists, shown in the designer's rule detail.
    /// </summary>
    string Rationale { get; }

    /// <summary>
    /// Analyzes a form and reports what the rule finds.
    /// </summary>
    /// <param name="context">
    /// The form under analysis.
    /// </param>
    /// <returns>
    /// Zero or more results, each anchored to a node by <see cref="LintResult.NodeId"/> where one
    /// applies.
    /// </returns>
    IEnumerable<LintResult> Analyze(LintContext context);
}
