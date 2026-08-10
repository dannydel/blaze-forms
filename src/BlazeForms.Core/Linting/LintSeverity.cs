namespace BlazeForms.Linting;

/// <summary>
/// How seriously the designer treats a lint result (PRD §8).
/// </summary>
public enum LintSeverity
{
    /// <summary>
    /// The form cannot be published while this result stands; the publish dialog lists each one
    /// with a jump-to-node action.
    /// </summary>
    Blocking,

    /// <summary>
    /// The form can publish, but the result is worth the author's attention.
    /// </summary>
    Advisory,
}
