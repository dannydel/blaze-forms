using System.Diagnostics.CodeAnalysis;
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
