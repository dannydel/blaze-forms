using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Playwright;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Axe accessibility scans against <c>/fill</c>, the shipped-default renderer (PRD §4.2, §11,
/// §14 #3; AGENTS.md invariant #4). <c>/fill-mud</c> is covered separately, non-gating
/// (<see cref="FillMudSmokeTests"/>) — MudBlazor's own accessibility is not this library's
/// contract.
/// </summary>
public sealed class FillAccessibilityTests : E2ETestBase
{
    [SuppressMessage(
        "Design",
        "CA1062:Validate arguments of public methods",
        Justification = "Both parameters are the collection fixtures xUnit itself supplies via ICollectionFixture<T> -- never null in practice -- and a base(...) initializer runs before any guard this body could add, so an in-body check here would be advisory only.")]
    public FillAccessibilityTests(SampleAppFixture sampleApp, BrowserFixture browserFixture)
        : base(sampleApp, browserFixture)
    {
    }

    [Fact]
    public async Task InitialRenderOfTheFillPageHasNoAccessibilityViolations()
    {
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl);

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/fill initial render (page 1)");
    }

    [Fact]
    public async Task RevealingTheConditionalDependentsCountFieldAddsItToTheDomAndStaysAccessible()
    {
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl);

        var dependentsCount = Page.GetByLabel("Number of dependents");

        // Hidden by logic means excluded from the DOM entirely (PRD §6), not merely visually
        // hidden -- so the locator resolves to nothing at all until the controlling field flips.
        Assert.Equal(0, await dependentsCount.CountAsync());

        await Page.GetByLabel("Yes", new PageGetByLabelOptions { Exact = true }).CheckAsync();
        await dependentsCount.WaitForAsync();

        Assert.Equal(1, await dependentsCount.CountAsync());
        await Assertions.Expect(dependentsCount).ToBeVisibleAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/fill with conditional field revealed");
    }

    [Fact]
    public async Task FailingValidationOnSubmitShowsAFocusableSummaryLinkedToARealField()
    {
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl);
        await SampleFormDriver.CompleteApplicantInformationPageAsync(Page);
        await SampleFormDriver.CompleteCoverageSelectionPageAsync(Page);

        // The review page's one required field, the coverage date range, is left empty -- Submit
        // must fail validation and render the focusable error summary rather than submit.
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit" }).ClickAsync();

        var summary = Page.GetByRole(AriaRole.Alert);
        await summary.WaitForAsync();

        var link = summary.Locator("a").First;
        var href = await link.GetAttributeAsync("href");
        Assert.NotNull(href);
        Assert.StartsWith("#", href, StringComparison.Ordinal);

        var targetId = href[1..];
        var targetExists = await Page.EvaluateAsync<bool>(
            "id => document.getElementById(id) !== null",
            targetId);
        Assert.True(targetExists, $"The error summary's first link points at \"#{targetId}\", which is not the id of any element on the page.");

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/fill validation error summary");
    }

    /// <summary>
    /// Drives the review page's "Estimated annual total" calc's own dependency — the coverage
    /// selection page's income field — through every page of the fill, and asserts the calc's
    /// <c>&lt;output&gt;</c> shows the computed text once the review page renders
    /// (<c>calc-engine-plan.md</c>, Increment C), with the whole path staying axe-clean.
    /// </summary>
    [Fact]
    public async Task FillingTheCalcsDependencyComputesTheDisplayedTotalAndStaysAccessible()
    {
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl);
        await SampleFormDriver.CompleteApplicantInformationPageAsync(Page);

        await SampleFormDriver.FillEstimatedMonthlyIncomeAsync(Page, "1000");
        await SampleFormDriver.CompleteCoverageSelectionPageAsync(Page);

        var expectedTotal = (1000m * 12m).ToString("F2", CultureInfo.CurrentCulture);
        var total = Page.GetByLabel("Estimated annual total");
        await Assertions.Expect(total).ToHaveTextAsync(expectedTotal);

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/fill review page with a computed calc total");
    }

    /// <summary>
    /// Drives the "Household members" repeating group (repeating-groups-plan.md, Increment C)
    /// keyboard-only: adds a row, fills its fields, adds a second row via keyboard, reorders it,
    /// then removes a row -- staying axe-clean throughout. Add/Remove/Move are all native
    /// <c>&lt;button&gt;</c>s, so <c>Tab</c>+<c>Enter</c> reaches every one of them exactly as a
    /// click would (AGENTS.md invariant #4).
    /// </summary>
    [Fact]
    public async Task RepeatingGroupRowsCanBeAddedFilledReorderedAndRemovedByKeyboardAndStayAccessible()
    {
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl);

        var addButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add Member" });
        Assert.True(await DesignerDriver.TabUntilFocusedAsync(Page, addButton));
        await Page.Keyboard.PressAsync("Enter");

        var firstRow = Page.GetByRole(AriaRole.Group, new PageGetByRoleOptions { Name = "Member 1" });
        await firstRow.WaitForAsync();
        await firstRow.GetByLabel("Full name").FillAsync("Alex Rivera");
        await firstRow.GetByLabel("Relationship to applicant").SelectOptionAsync(new SelectOptionValue { Label = "Spouse" });
        await firstRow.GetByLabel("Weekly income").FillAsync("500");

        // A second row, added via keyboard alone -- Add sits after the rows, so Tab reaches it
        // again from wherever the first row's own last edit left focus.
        Assert.True(await DesignerDriver.TabUntilFocusedAsync(Page, addButton));
        await Page.Keyboard.PressAsync("Enter");

        var secondRow = Page.GetByRole(AriaRole.Group, new PageGetByRoleOptions { Name = "Member 2" });
        await secondRow.WaitForAsync();
        await secondRow.GetByLabel("Full name").FillAsync("Sam Rivera");

        // Reorder: move Member 2 up one position via its own Move up button.
        var moveUpSecond = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Move Member 2 up" });
        Assert.True(await DesignerDriver.TabUntilFocusedAsync(Page, moveUpSecond));
        await Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(Page.GetByRole(AriaRole.Group, new PageGetByRoleOptions { Name = "Member 1" }).GetByLabel("Full name"))
            .ToHaveValueAsync("Sam Rivera");

        // Remove the (now first) row via its own Remove button, reached by keyboard alone.
        var removeFirst = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Remove Member 1" });
        Assert.True(await DesignerDriver.TabUntilFocusedAsync(Page, removeFirst));
        await Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(Page.GetByRole(AriaRole.Group, new PageGetByRoleOptions { Name = "Member 1" }).GetByLabel("Full name"))
            .ToHaveValueAsync("Alex Rivera");
        await Assertions.Expect(Page.GetByRole(AriaRole.Group, new PageGetByRoleOptions { Name = "Member 2" })).Not.ToBeVisibleAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/fill with repeating-group rows added, filled, reordered, and removed");
    }

    [Fact]
    public async Task TabbingFromTheTopReachesFirstPageControlsWithoutATrap()
    {
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl);
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/fill before keyboard traversal");

        var fullLegalNameId = await Page.GetByLabel("Full legal name").GetAttributeAsync("id");
        Assert.NotNull(fullLegalNameId);

        var focusedIds = new List<string?>();

        for (var tabIndex = 0; tabIndex < 10; tabIndex++)
        {
            await Page.Keyboard.PressAsync("Tab");
            var focusedId = await Page.EvaluateAsync<string?>(
                "() => document.activeElement instanceof HTMLElement ? document.activeElement.id : null");
            focusedIds.Add(focusedId);
        }

        Assert.Contains(fullLegalNameId, focusedIds);

        // No keyboard trap: ten Tab presses from the top must reach more than one focus target.
        Assert.True(
            focusedIds.Distinct().Count() > 1,
            "Ten Tab presses from the top of the page never left one focus target -- possible keyboard trap.");
    }
}
