using BlazeForms.Definitions;

namespace BlazeForms.Linting;

/// <summary>
/// Everything a rule needs to analyze a form (PRD §8). A record so it grows by adding members
/// without breaking the rules that ignore them.
/// </summary>
public sealed record LintContext
{
    /// <summary>
    /// The definition under analysis.
    /// </summary>
    public required FormDefinition Definition { get; init; }
}
