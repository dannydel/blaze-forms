using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Linting;

namespace BlazeForms.Core.Tests.Linting;

/// <summary>
/// Shared scaffolding for the per-rule tests: each built-in rule is fetched from the public
/// <see cref="LintRuleRegistry.Default"/> by ID, and definitions are assembled from a flat node
/// list wrapped in one page and section.
/// </summary>
internal static class RuleTestHelpers
{
    internal static ILintRule RuleFor(string id) =>
        LintRuleRegistry.Default.Single(rule => string.Equals(rule.Id, id, StringComparison.Ordinal));

    internal static IReadOnlyList<LintResult> Analyze(ILintRule rule, FormDefinition definition) =>
        [.. rule.Analyze(new LintContext { Definition = definition })];

    internal static FormDefinition Definition(
        IReadOnlyList<FormNode> nodes,
        IReadOnlyList<ValidationRule>? validationRules = null) => new()
    {
        Id = "form-rule-test",
        Name = "Rule test",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "Page one",
                Sections = [new FormSection { Id = "section-1", Title = "Section one", Nodes = nodes }],
            },
        ],
        ValidationRules = validationRules ?? [],
    };
}
