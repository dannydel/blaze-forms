using System.Diagnostics.CodeAnalysis;
using Microsoft.Playwright;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Axe accessibility scans against the empty and error states no scenario elsewhere in this suite
/// ever puts a page into (docs/accessibility-statement-plan.md, Increment B2b/c/d): a repeating
/// group at zero rows, a page whose only section is empty, and a single field left in
/// <c>aria-invalid</c> with its own inline message while the aggregate error summary is not
/// present. The library's own "no results" empty state (B2a) lives alongside
/// <see cref="LibraryAccessibilityTests"/> instead, since it is that suite's own fixture this
/// scenario shares.
/// </summary>
public sealed class EmptyAndErrorStateAccessibilityTests : E2ETestBase
{
    [SuppressMessage(
        "Design",
        "CA1062:Validate arguments of public methods",
        Justification = "Both parameters are the collection fixtures xUnit itself supplies via ICollectionFixture<T> -- never null in practice -- and a base(...) initializer runs before any guard this body could add, so an in-body check here would be advisory only.")]
    public EmptyAndErrorStateAccessibilityTests(SampleAppFixture sampleApp, BrowserFixture browserFixture)
        : base(sampleApp, browserFixture)
    {
    }

    /// <summary>
    /// The reference form's "Household members" repeating group (<c>EnrollmentForm.cs</c>) ships
    /// with <c>MinRows = 0</c>, so <c>/fill</c>'s own initial render already puts it at zero rows
    /// — this scenario asserts that explicitly (no "Member 1" group exists, only the group's own
    /// fieldset and "Add Member" button) rather than relying on it as an unstated side effect of
    /// some other scenario's own setup. No axe scan here: this is the exact same DOM state
    /// <see cref="FillAccessibilityTests.InitialRenderOfTheFillPageHasNoAccessibilityViolations"/>
    /// already scans (unmodified /fill page 1) — this scenario's own value is documenting and
    /// pinning the zero-row state explicitly, not a second scan of it.
    /// </summary>
    [Fact]
    public async Task RepeatingGroupAtZeroRowsIsExplicitlyPresentOnInitialRender()
    {
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl);

        var group = Page.GetByRole(AriaRole.Group, new PageGetByRoleOptions { Name = "Household members" });
        await group.WaitForAsync();
        await Assertions.Expect(group.GetByRole(AriaRole.Group)).ToHaveCountAsync(0);
        await Assertions.Expect(group.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add Member" })).ToBeVisibleAsync();
    }

    /// <summary>
    /// <c>PageTabStrip</c>'s own "Add blank section" affordance (only offered while the active
    /// page has none at all) reaches a section with zero fields in it — a state the palette-add
    /// flow every other designer scenario in this suite uses never leaves behind, since a palette
    /// add always creates its first field in the same action as its first section.
    /// </summary>
    [Fact]
    public async Task PageWithOnlyAnEmptySectionHasNoAccessibilityViolations()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add blank section" }).ClickAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add blank section" })).Not.ToBeVisibleAsync();
        await Assertions.Expect(DesignerDriver.CanvasRows(Page)).ToHaveCountAsync(0);

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design page with only an empty section");
    }

    /// <summary>
    /// Fails whole-form validation on the review page's own one required field (the coverage date
    /// range), which renders both the aggregate error summary and the field's own inline
    /// <c>aria-invalid</c> state together — then navigates away and back, which
    /// <c>FormRenderer.GoToPage</c> always resets <c>_showSummary</c> on but never touches the
    /// underlying per-field <see cref="Dictionary{TKey,TValue}"/> of errors, leaving the field's
    /// own inline error live with the aggregate summary gone. This is the one state nowhere else
    /// in this suite reaches: every other scenario's error summary and inline error appear
    /// together, since both come from the same validation pass.
    /// </summary>
    [Fact]
    public async Task SingleFieldInvalidWithoutTheErrorSummaryHasNoAccessibilityViolations()
    {
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl);
        await SampleFormDriver.CompleteApplicantInformationPageAsync(Page);
        await SampleFormDriver.CompleteCoverageSelectionPageAsync(Page);

        // The review page's coverage date range is left empty -- Submit fails whole-form
        // validation and renders both the summary and this field's own inline error together.
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit" }).ClickAsync();
        await Page.GetByRole(AriaRole.Alert).WaitForAsync();

        // DateRangeField.razor's own aria-describedby lives on the wrapping <fieldset> (no single
        // input speaks for a two-part range) -- a native, implicit role="group" whose own
        // accessible name comes from its <legend>, the field's own label; aria-invalid is on each
        // of the two inner inputs individually.
        var dateRangeGroup = Page.GetByRole(AriaRole.Group, new PageGetByRoleOptions { Name = "Requested coverage dates" });
        var startDate = Page.GetByLabel("Start date");
        await Assertions.Expect(startDate).ToHaveAttributeAsync("aria-invalid", "true");

        // Navigate away and back -- GoToPage always clears _showSummary on every transition, but
        // never touches the per-field error dictionary the inline message reads from, so the
        // summary disappears while the field's own aria-invalid and inline message survive.
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Previous" }).ClickAsync();
        await Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Coverage selection" }).WaitForAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next" }).ClickAsync();
        await Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Review and submit" }).WaitForAsync();

        await Assertions.Expect(Page.GetByRole(AriaRole.Alert)).Not.ToBeVisibleAsync();
        await Assertions.Expect(startDate).ToHaveAttributeAsync("aria-invalid", "true");

        var describedBy = await dateRangeGroup.GetAttributeAsync("aria-describedby");
        Assert.NotNull(describedBy);
        var errorId = describedBy.Split(' ').FirstOrDefault(id => id.Contains("error", StringComparison.Ordinal));
        Assert.NotNull(errorId);
        await Assertions.Expect(Page.Locator($"#{errorId}")).ToBeVisibleAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/fill single field aria-invalid with inline message, no error summary");
    }
}
