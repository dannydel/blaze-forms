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
    /// CROSS-REFERENCE (by symbol name, not line number, since line numbers drift):
    /// <c>eng/demo-smoke-test.mjs</c>'s own <c>wcag22AaTags</c> constant duplicates this exact
    /// five-element list for the WASM demo's own axe scan — there is no shared source across the
    /// C#/Node boundary, so if this list ever changes, that one needs the same change by hand.
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
    /// axe-core's own <c>best-practice</c> tag, deliberately never folded into
    /// <see cref="Wcag22AaTags"/>'s own <c>RunOnly</c> list as a single undifferentiated tag set
    /// (resolved decision 6, docs/accessibility-statement-plan.md): a best-practice finding says
    /// "axe has an opinion", never "this page fails WCAG 2.2 AA", and the two must never share one
    /// failure message. A spike run of every scenario in this suite plus a representative slice of
    /// the Designer's own dialogs — scanned with <em>this tag alone</em>, the same filter this
    /// file's own code applies below — found zero best-practice violations, full stop, across
    /// every one of them. <see cref="BestPracticeAllowList"/> therefore ships empty.
    /// </summary>
    private const string BestPracticeTag = "best-practice";

    /// <summary>
    /// Verified against the shipped axe-core rule set: no rule carries both
    /// <see cref="BestPracticeTag"/> and any tag in <see cref="Wcag22AaTags"/>, so a single scan
    /// requesting both tag families in one <c>RunOnly</c> call partitions cleanly on
    /// <see cref="AxeResultItem.Tags"/> — every violation is unambiguously either an AA violation
    /// or a best-practice finding, never both. This is what lets <see cref="AssertNoViolationsAsync"/>
    /// run axe exactly once per scenario instead of twice.
    /// </summary>
    private static readonly List<string> AllScannedTags = [.. Wcag22AaTags, BestPracticeTag];

    /// <summary>
    /// Rule ids allow-listed out of the best-practice check, one required justification comment
    /// per entry (resolved decision 6) — a stale, unjustified allow-list is worse than no signal
    /// at all. Ships empty: the spike (this file's own remarks on <see cref="BestPracticeTag"/>)
    /// found nothing to allow-list in the gated Renderer/Designer surface.
    /// </summary>
    private static readonly List<string> BestPracticeAllowList = [];

    /// <summary>
    /// Scans <paramref name="page"/> in its current state against the WCAG 2.2 AA rule set AND
    /// axe-core's <c>best-practice</c> rule set, in one axe run, and fails the test if either
    /// check finds anything — with a distinct failure message per check, so a CI failure never
    /// conflates "this page fails WCAG 2.2 AA" (fatal to the AA claim) with "axe has a new opinion
    /// about this page" (fatal to the build the same way, but a different kind of regression;
    /// resolved decision 6 keeps the two conceptually separate even though both are enforced here).
    /// The AA check runs first and reports on its own if it finds anything, since a page already
    /// failing AA has a more urgent problem than a best-practice regression.
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
            RunOnly = new RunOnlyOptions { Type = "tag", Values = AllScannedTags },
        };

        var results = await page.RunAxe(options).ConfigureAwait(false);

        var aaViolations = results.Violations.Where(v => !v.Tags.Contains(BestPracticeTag)).ToArray();
        var bestPracticeViolations = results.Violations.Where(v => v.Tags.Contains(BestPracticeTag)).ToArray();

        AssertNoAaViolations(aaViolations, scenario);
        AssertNoBestPracticeRegressions(bestPracticeViolations, scenario);
    }

    private static void AssertNoAaViolations(AxeResultItem[] violations, string scenario)
    {
        if (violations.Length == 0)
        {
            return;
        }

        var report = string.Join(Environment.NewLine + Environment.NewLine, violations.Select(FormatViolation));

        Assert.Fail(
            $"axe found {violations.Length} WCAG 2.2 AA violation(s) in \"{scenario}\":{Environment.NewLine}{report}");
    }

    private static void AssertNoBestPracticeRegressions(AxeResultItem[] violations, string scenario)
    {
        var unallowed = violations.Where(v => !BestPracticeAllowList.Contains(v.Id)).ToArray();

        if (unallowed.Length == 0)
        {
            return;
        }

        var report = string.Join(Environment.NewLine + Environment.NewLine, unallowed.Select(FormatViolation));

        Assert.Fail(
            $"axe found {unallowed.Length} new best-practice finding(s) (not WCAG 2.2 AA violations) in \"{scenario}\":{Environment.NewLine}{report}{Environment.NewLine}{Environment.NewLine}Either fix it, or add the rule id to AccessibilityAssertions.BestPracticeAllowList with a one-line justification.");
    }

    private static string FormatViolation(AxeResultItem violation)
    {
        var targets = string.Join(", ", violation.Nodes.Select(node => node.Target.ToString()));
        return $"[{violation.Impact}] {violation.Id} — {violation.Help} ({violation.HelpUrl}){Environment.NewLine}  targets: {targets}";
    }
}
