using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Runs axe-core against a page's current state and asserts zero violations — the shared
/// assertion every scenario in this suite ends with (PRD §11, §14 #3; AGENTS.md invariant #4).
/// </summary>
internal static class AccessibilityAssertions
{
    /// <summary>
    /// The rule tags PRD §11's "zero WCAG 2.2 AA violations" success criterion (§14 #3) maps to:
    /// axe-core still tags the WCAG 2.0/2.1 Level A and AA rule sets separately from the 2.2 AA
    /// increment, so all five are named explicitly rather than relying on a single umbrella tag.
    /// </summary>
    private static readonly List<string> Wcag22AaTags =
    [
        "wcag2a",
        "wcag2aa",
        "wcag21a",
        "wcag21aa",
        "wcag22aa",
    ];

    /// <summary>
    /// Scans <paramref name="page"/> in its current state against the WCAG 2.2 AA rule set and
    /// fails the test, with every violation's rule id, impact, help text, and offending
    /// selectors, if axe found any.
    /// </summary>
    /// <param name="page">The page to scan, in whatever state the caller has driven it to.</param>
    /// <param name="scenario">
    /// A short human-readable label for the state under test (e.g. "validation error summary"),
    /// included in the failure message so a CI failure names the scenario without anyone having
    /// to cross-reference the test method.
    /// </param>
    public static async Task AssertNoViolationsAsync(IPage page, string scenario)
    {
        var options = new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = Wcag22AaTags },
        };

        var results = await page.RunAxe(options).ConfigureAwait(false);

        if (results.Violations.Length == 0)
        {
            return;
        }

        var report = string.Join(Environment.NewLine + Environment.NewLine, results.Violations.Select(FormatViolation));

        Assert.Fail(
            $"axe found {results.Violations.Length} WCAG 2.2 AA violation(s) in \"{scenario}\":{Environment.NewLine}{report}");
    }

    private static string FormatViolation(AxeResultItem violation)
    {
        var targets = string.Join(", ", violation.Nodes.Select(node => node.Target.ToString()));
        return $"[{violation.Impact}] {violation.Id} — {violation.Help} ({violation.HelpUrl}){Environment.NewLine}  targets: {targets}";
    }
}
