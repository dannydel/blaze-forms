using BlazeForms.Definitions;
using BlazeForms.Linting;

namespace BlazeForms.Core.Tests;

/// <summary>
/// PRD §8: the linter engine composes rules, enriches each result with its location, and answers
/// the publish gate.
/// </summary>
public sealed class FormLinterTests
{
    private sealed class FakeRule : ILintRule
    {
        public string Id => "FAKE-01";

        public LintSeverity Severity => LintSeverity.Advisory;

        public string Rationale => "A stand-in rule for composition tests.";

        public IEnumerable<LintResult> Analyze(LintContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            yield return new LintResult
            {
                RuleId = Id,
                Severity = Severity,
                Message = "The fake rule fired.",
            };
        }
    }

    [Fact]
    public void CreateDefaultCarriesTheFiveBuiltInRulesInIdOrder()
    {
        var rules = LintRuleRegistry.Default;

        Assert.Equal(
            [LintRuleIds.A11y01, LintRuleIds.Fr03, LintRuleIds.A11y06, LintRuleIds.A11y08, LintRuleIds.A11y09],
            rules.Select(rule => rule.Id));
    }

    [Fact]
    public void EngineEnrichesPageAndSectionFromTheAnchoredNode()
    {
        var results = FormLinter.CreateDefault().Lint(TestDefinitions.LintFixtureDefinition);

        var unlabeled = Assert.Single(
            results,
            result => result.RuleId == LintRuleIds.A11y01 && result.NodeId == "fixture-unlabeled");

        Assert.Equal(0, unlabeled.PageIndex);
        Assert.Equal(0, unlabeled.SectionIndex);
    }

    [Fact]
    public void ResultWithoutANodeIsLeftUnenriched()
    {
        var linter = new FormLinter([new FakeRule()]);

        var result = Assert.Single(linter.Lint(TestDefinitions.CleanDefinition));

        Assert.Null(result.NodeId);
        Assert.Null(result.PageIndex);
        Assert.Null(result.SectionIndex);
    }

    [Fact]
    public void CustomCompositionSurfacesBothBuiltInAndCustomResults()
    {
        var linter = new FormLinter([.. LintRuleRegistry.Default, new FakeRule()]);

        var results = linter.Lint(TestDefinitions.LintFixtureDefinition);

        Assert.Contains(results, result => result.RuleId == "FAKE-01");
        Assert.Contains(results, result => result.RuleId == LintRuleIds.A11y01);
    }

    [Fact]
    public void CleanDefinitionProducesNoResultsAndNoBlockingIssues()
    {
        var results = FormLinter.CreateDefault().Lint(TestDefinitions.CleanDefinition);

        Assert.Empty(results);
        Assert.False(results.HasBlockingIssues());
    }

    [Fact]
    public void FixtureDefinitionHasBlockingIssues()
    {
        var results = FormLinter.CreateDefault().Lint(TestDefinitions.LintFixtureDefinition);

        Assert.True(results.HasBlockingIssues());
        Assert.All(results.Blocking(), result => Assert.Equal(LintSeverity.Blocking, result.Severity));
        Assert.Contains(results.Blocking(), result => result.RuleId == LintRuleIds.A11y01);
        Assert.Contains(results.Blocking(), result => result.RuleId == LintRuleIds.Fr03);
    }

    [Fact]
    public void LintAndConstructorRejectNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new FormLinter(null!));
        Assert.Throws<ArgumentNullException>(() => FormLinter.CreateDefault().Lint(null!));
    }

    [Fact]
    public void ExtensionsRejectNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ((IEnumerable<LintResult>)null!).HasBlockingIssues());
        Assert.Throws<ArgumentNullException>(() => ((IEnumerable<LintResult>)null!).Blocking().ToList());
    }
}
