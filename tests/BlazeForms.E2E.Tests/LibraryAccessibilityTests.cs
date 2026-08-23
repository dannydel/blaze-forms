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

    /// <summary>
    /// The library's "no results" empty state (docs/accessibility-statement-plan.md, Increment
    /// B2a) — a search term matching nothing, not a genuinely form-less store: the seeded
    /// reference form this whole suite otherwise depends on existing means there is no cheap way
    /// to reach <c>FormLibrary</c>'s own zero-forms branch (<c>FormLibraryEmpty</c>) without a
    /// second, seedless host route; the filtered-to-nothing branch (<c>FormLibraryNoResults</c>)
    /// needs no such affordance and is the honest, reachable-today empty state to scan.
    /// </summary>
    [Fact]
    public async Task SearchingToNoResultsHasNoAccessibilityViolations()
    {
        await GotoLibraryAsync();

        await Page.GetByLabel("Search forms").FillAsync("no form named this exists anywhere");
        await Assertions.Expect(Page.GetByText("No forms match your search and filters.")).ToBeVisibleAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/library filtered to zero results");
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
