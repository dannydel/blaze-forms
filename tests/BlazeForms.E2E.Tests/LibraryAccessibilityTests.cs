using System.Diagnostics.CodeAnalysis;
using Microsoft.Playwright;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Axe accessibility scans against <c>/library</c>, the shipped-default form-management surface
/// (PRD §4.4, §11, §14 #3; AGENTS.md invariant #4) — the second half of the designer surface the
/// Playwright + axe CI gate now covers, alongside <see cref="DesignAccessibilityTests"/>.
/// </summary>
public sealed class LibraryAccessibilityTests : E2ETestBase
{
    [SuppressMessage(
        "Design",
        "CA1062:Validate arguments of public methods",
        Justification = "Both parameters are the collection fixtures xUnit itself supplies via ICollectionFixture<T> -- never null in practice -- and a base(...) initializer runs before any guard this body could add, so an in-body check here would be advisory only.")]
    public LibraryAccessibilityTests(SampleAppFixture sampleApp, BrowserFixture browserFixture)
        : base(sampleApp, browserFixture)
    {
    }

    private async Task GotoLibraryAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/library").ConfigureAwait(false);
        // The seeded reference enrollment form (Program.cs) is always present, so the library is
        // never showing its own loading or empty state by the time a scenario below scans it.
        // The card's own open button also carries this text ("Open 'Benefits Enrollment' in the
        // designer"), so this waits on the heading role specifically rather than a bare text
        // match, which would resolve to both and fail Playwright's strict mode.
        await Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Benefits Enrollment" }).WaitForAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task InitialRenderInCardsViewHasNoAccessibilityViolations()
    {
        await GotoLibraryAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/library initial render (cards view)");
    }

    [Fact]
    public async Task SearchingAndFilteringHaveNoAccessibilityViolations()
    {
        await GotoLibraryAsync();

        await Page.GetByLabel("Search forms").FillAsync("Benefits");
        await Assertions.Expect(Page.GetByText("Showing 1 of")).ToBeVisibleAsync();
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/library filtered by search term");

        await Page.GetByLabel("Search forms").FillAsync("");
        await Page.GetByLabel("Status").SelectOptionAsync(new SelectOptionValue { Label = "Published" });
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/library filtered by status");
    }

    [Fact]
    public async Task TableViewHasNoAccessibilityViolations()
    {
        await GotoLibraryAsync();

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Table" }).ClickAsync();
        await Page.Locator("table.bf-form-table").WaitForAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/library table view");
    }
}
