using System.Diagnostics.CodeAnalysis;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// A non-gating smoke check on <c>/fill-mud</c> — the PRD §10/§14 #4 honesty test that swapping
/// every field component for a MudBlazor one takes only a registry registration. MudBlazor's own
/// accessibility is not this library's contract (that gate applies to the shipped-default
/// renderer only, see <see cref="FillAccessibilityTests"/>), so this class asserts only that the
/// page loads and renders MudBlazor markup, and logs — but never asserts on — an axe scan.
/// </summary>
public sealed class FillMudSmokeTests : E2ETestBase
{
    private readonly ITestOutputHelper _output;

    [SuppressMessage(
        "Design",
        "CA1062:Validate arguments of public methods",
        Justification = "sampleApp and browserFixture are the collection fixtures xUnit itself supplies via ICollectionFixture<T> -- never null in practice -- and a base(...) initializer runs before any guard this body could add, so an in-body check on them here would be advisory only.")]
    public FillMudSmokeTests(SampleAppFixture sampleApp, BrowserFixture browserFixture, ITestOutputHelper output)
        : base(sampleApp, browserFixture)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    [Fact]
    public async Task FillMudLoadsAndRendersMudBlazorMarkup()
    {
        // Unlike /fill, /fill-mud carries no ?respondent= override -- it always fills under the
        // fixed "demo-respondent-mud" key -- but this is the only test in the suite that visits
        // it, so there is nothing else it could collide with.
        await Page.GotoAsync($"{BaseUrl}/fill-mud");

        await Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Benefits Enrollment" }).WaitForAsync();

        // MudBlazor's inputs render inside its own `mud-*`-classed wrapper markup -- present here
        // is the whole point of /fill-mud (PRD §14 #4); asserting on it (rather than on
        // BlazeForms' own `bf-*` classes) is what makes this a MudBlazor smoke check and not a
        // duplicate of FillAccessibilityTests.
        Assert.True(await Page.Locator(".mud-input-control").First.IsVisibleAsync());

        // Deliberately non-asserting: MudBlazor's own WCAG compliance is not this library's
        // contract (AGENTS.md invariant #4 gates BlazeForms.Renderer's shipped default only), so
        // a real MudBlazor a11y defect here must never fail this suite. Logged, not asserted,
        // purely so a maintainer curious about MudBlazor's state can see it in the test output.
        var results = await Page.RunAxe();
        _output.WriteLine(
            $"[log-only] axe found {results.Violations.Length} violation(s) on /fill-mud (not asserted; MudBlazor is out of contract).");
    }
}
