using BlazeForms.Linting.Rules;

namespace BlazeForms.Linting;

/// <summary>
/// The built-in lint rules (PRD §8), exposed in ID order. The backing array is the single source
/// of the default set; <see cref="FormLinter.CreateDefault"/> and any host composing its own rules
/// on top of the built-ins read from here.
/// </summary>
public static class LintRuleRegistry
{
    private static readonly ILintRule[] DefaultBacking =
    [
        new A11yLabelRule(),
        new DanglingReferenceRule(),
        new RemedyMessageRule(),
        new HeadingLevelRule(),
        new LinkTextRule(),
    ];

    /// <summary>
    /// The five built-in rules — A11Y-01, FR-03, A11Y-06, A11Y-08, A11Y-09 — in that order.
    /// </summary>
    public static IReadOnlyList<ILintRule> Default => DefaultBacking;
}
