namespace BlazeForms.Linting;

/// <summary>
/// The public registry of built-in lint rule identifiers (PRD §8). The IDs are documented,
/// stable, and quoted in the designer's rule detail; a host or contributor adding a rule brings
/// its own ID.
/// </summary>
public static class LintRuleIds
{
    /// <summary>
    /// An input node has no label; a placeholder is not a label (Blocking).
    /// </summary>
    public const string A11y01 = "A11Y-01";

    /// <summary>
    /// A rule references a field that no longer exists (Blocking).
    /// </summary>
    public const string Fr03 = "FR-03";

    /// <summary>
    /// A validation message states no remedy (Advisory).
    /// </summary>
    public const string A11y06 = "A11Y-06";

    /// <summary>
    /// A heading level skips a rung (Advisory).
    /// </summary>
    public const string A11y08 = "A11Y-08";

    /// <summary>
    /// A Markdown link's text does not describe its destination (Advisory).
    /// </summary>
    public const string A11y09 = "A11Y-09";
}
