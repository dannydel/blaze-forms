using BlazeForms.Definitions;

namespace BlazeForms.Linting;

/// <summary>
/// Runs a set of <see cref="ILintRule"/> over a definition and enriches each result with its
/// location (PRD §8). Rules report a <see cref="LintResult.NodeId"/>; the engine resolves that to
/// a page and section via <see cref="FormDefinitionExtensions.LocateNode"/>, so a rule never has
/// to walk the tree to describe where its finding lives.
/// </summary>
public sealed class FormLinter
{
    private readonly IReadOnlyList<ILintRule> _rules;

    /// <summary>
    /// Creates a linter over an explicit set of rules, so a host can compose the built-ins with
    /// its own — <c>new FormLinter([.. LintRuleRegistry.Default, myRule])</c>.
    /// </summary>
    /// <param name="rules">
    /// The rules to run, in the order results should be produced.
    /// </param>
    public FormLinter(IEnumerable<ILintRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        _rules = [.. rules];
    }

    /// <summary>
    /// Creates a linter over the built-in rule set (<see cref="LintRuleRegistry.Default"/>).
    /// </summary>
    /// <returns>
    /// A linter carrying the five built-in rules.
    /// </returns>
    public static FormLinter CreateDefault() => new(LintRuleRegistry.Default);

    /// <summary>
    /// Analyzes a definition with every configured rule.
    /// </summary>
    /// <param name="definition">
    /// The definition to lint.
    /// </param>
    /// <returns>
    /// Every result from every rule, in rule order, each enriched with its page and section where
    /// it anchors to a node.
    /// </returns>
    public IReadOnlyList<LintResult> Lint(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var context = new LintContext { Definition = definition };
        var results = new List<LintResult>();

        foreach (var rule in _rules)
        {
            foreach (var result in rule.Analyze(context))
            {
                results.Add(Enrich(result, definition));
            }
        }

        return results;
    }

    private static LintResult Enrich(LintResult result, FormDefinition definition)
    {
        if (result.NodeId is null)
        {
            return result;
        }

        if (definition.LocateNode(result.NodeId) is not (int pageIndex, int sectionIndex))
        {
            return result;
        }

        return result with { PageIndex = pageIndex, SectionIndex = sectionIndex };
    }
}
