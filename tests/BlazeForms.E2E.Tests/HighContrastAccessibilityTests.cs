using System.Diagnostics.CodeAnalysis;
using Microsoft.Playwright;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Exercises Increment C's two high-contrast mechanisms (docs/accessibility-statement-plan.md,
/// resolved decision 1) end to end: the opt-in <c>[data-bf-theme="high-contrast"]</c> theme (the
/// marketable half) and <c>forced-colors: active</c> emulation (the correctness half). Both are
/// re-scans of the same shipped-default surfaces the rest of this suite already covers, plus one
/// scenario that proves the specific defect Increment C1 fixes — the designer's selected-row cue
/// stays distinguishable once the UA replaces every author color — and one that proves the two
/// mechanisms combine correctly rather than the opt-in theme's own higher-or-equal-specificity
/// token re-declaration silently defeating the forced-colors hardening.
///
/// Every scenario that applies the theme or forced colors asserts a POSITIVE PRECONDITION before
/// scanning — a real computed-style check that the mechanism actually took effect, not just that
/// the API call that was supposed to enable it did not throw. A scan that silently ran against the
/// default theme (because the attribute never reached a live <c>&lt;html&gt;</c>, say) would still
/// report zero axe violations, since the default theme is itself axe-clean — a scan with no
/// precondition proves nothing about the theme at all.
/// </summary>
public sealed class HighContrastAccessibilityTests : E2ETestBase
{
    [SuppressMessage(
        "Design",
        "CA1062:Validate arguments of public methods",
        Justification = "Both parameters are the collection fixtures xUnit itself supplies via ICollectionFixture<T> -- never null in practice -- and a base(...) initializer runs before any guard this body could add, so an in-body check here would be advisory only.")]
    public HighContrastAccessibilityTests(SampleAppFixture sampleApp, BrowserFixture browserFixture)
        : base(sampleApp, browserFixture)
    {
    }

    /// <summary>
    /// Sets <c>data-bf-theme="high-contrast"</c> on the live <c>&lt;html&gt;</c> AFTER navigation
    /// has completed, via a plain <see cref="IPage.EvaluateAsync{T}(string)"/> — NOT
    /// <see cref="IPage.AddInitScriptAsync(string)"/>, which runs at document-start, before
    /// Blazor (or even the browser's own parser) has created <c>document.documentElement</c>; an
    /// init script referencing it throws a <c>TypeError</c> that Playwright swallows as a page
    /// error, so every scenario that used to rely on it was silently scanning the DEFAULT theme.
    /// Waits (via <see cref="IPage.WaitForFunctionAsync(string, object, PageWaitForFunctionOptions)"/>,
    /// which polls rather than racing a fixed delay) for a real re-themed token to actually show up
    /// in <c>getComputedStyle</c> before returning — the positive precondition every caller of this
    /// method gets for free, rather than each scenario re-deriving its own.
    /// </summary>
    private async Task ApplyHighContrastThemeAsync()
    {
        await Page.EvaluateAsync("document.documentElement.setAttribute('data-bf-theme', 'high-contrast')").ConfigureAwait(false);

        await Page.WaitForFunctionAsync(
            "() => document.documentElement.getAttribute('data-bf-theme') === 'high-contrast' " +
            "&& getComputedStyle(document.documentElement).getPropertyValue('--bf-color-primary').trim() === '#0b3d91'").ConfigureAwait(false);
    }

    /// <summary>
    /// Turns on <c>forced-colors: active</c> emulation and waits for the media feature to actually
    /// report active before returning — the positive precondition for every forced-colors scenario
    /// below, the same reason <see cref="ApplyHighContrastThemeAsync"/> waits for a real token
    /// change rather than trusting that the enabling call alone did what it says.
    /// </summary>
    private async Task ApplyForcedColorsAsync()
    {
        await Page.EmulateMediaAsync(new PageEmulateMediaOptions { ForcedColors = ForcedColors.Active }).ConfigureAwait(false);
        await Page.WaitForFunctionAsync("() => window.matchMedia('(forced-colors: active)').matches").ConfigureAwait(false);
    }

    [Fact]
    public async Task FillPageUnderTheHighContrastThemeHasNoAccessibilityViolations()
    {
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl);
        await ApplyHighContrastThemeAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/fill under [data-bf-theme=\"high-contrast\"]");
    }

    [Fact]
    public async Task DesignPageUnderTheHighContrastThemeHasNoAccessibilityViolations()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await ApplyHighContrastThemeAsync();
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Text");

        await Assertions.Expect(DesignerDriver.CanvasRows(Page)).ToHaveCountAsync(1);
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design under [data-bf-theme=\"high-contrast\"]");
    }

    [Fact]
    public async Task LibraryPageUnderTheHighContrastThemeHasNoAccessibilityViolations()
    {
        await Page.GotoAsync($"{BaseUrl}/library");
        await Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Benefits Enrollment" }).WaitForAsync();
        await ApplyHighContrastThemeAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/library under [data-bf-theme=\"high-contrast\"]");
    }

    /// <summary>
    /// Combines the two things "forced-colors: active + axe" needs to cover with the fewest
    /// full app boots this suite's collection-scoped Blazor Server host can afford: a plain
    /// <c>/fill</c> scan (the renderer's own surface) folded into this class's setup rather than
    /// its own separate <see cref="E2ETestBase.Page"/> session, and the axe scan of the exact
    /// defect below sharing that same forced-colors emulation on the SAME page/context (Playwright
    /// still tears down and re-navigates cleanly between the two). Every extra
    /// <c>GotoFillAsync</c>/<c>GotoNewDesignAsync</c> in this suite opens one more SignalR circuit
    /// against the one sample-host process every test in this collection shares (PRD §9's own
    /// scoped-fixture tradeoff) -- consolidating here rather than adding yet another one-page-one-
    /// test class is a deliberate stability choice, not a coverage cut.
    /// </summary>
    [Fact]
    public async Task FillPageUnderForcedColorsHasNoAccessibilityViolations()
    {
        await ApplyForcedColorsAsync();
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl);

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/fill under forced-colors: active");
    }

    /// <summary>
    /// The exact defect Increment C1 fixes: under <c>forced-colors: active</c>, a selected canvas
    /// row's <c>--bf-canvas-row-selected-bg</c> background collapses to the UA's flat <c>Canvas</c>
    /// color, indistinguishable from an unselected row's own background — unless the row's own CSS
    /// (<c>CanvasNodeRow.razor.css</c>) also widens a structural, non-color <c>border-inline-start</c>
    /// under the same media query. Adds two fields so the row the second add leaves selected has an
    /// unselected sibling to compare against, then tabs focus away from the canvas entirely first —
    /// selecting a node moves real DOM focus to the properties panel's own Label input
    /// (<c>PropertiesPanel</c>'s documented focus-steal), so this proves the cue survives with
    /// nothing left focused on the canvas, not merely while a :focus-visible outline is also
    /// present. Per the caution in docs/accessibility-statement-plan.md's own Increment C3 section,
    /// this never clicks a row to place or read focus.
    /// </summary>
    [Fact]
    public async Task SelectedCanvasRowStaysDistinguishableUnderForcedColors()
    {
        await ApplyForcedColorsAsync();
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddLabeledFieldFromPaletteAsync(Page, "Text", "First field");
        await DesignerDriver.AddLabeledFieldFromPaletteAsync(Page, "Text", "Second field");

        var selectedRow = DesignerDriver.CanvasRowByLabel(Page, "Second field");
        var unselectedRow = DesignerDriver.CanvasRowByLabel(Page, "First field");

        await Assertions.Expect(selectedRow).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(unselectedRow).ToHaveAttributeAsync("aria-selected", "false");

        // Nothing on the canvas holds real DOM focus once this fires -- the row's own selected
        // styling, not a co-incidental :focus-visible outline, is what the assertion below proves.
        await Page.Keyboard.PressAsync("Tab");

        var selectedBorderWidth = await BorderInlineStartWidthPxAsync(selectedRow);
        var unselectedBorderWidth = await BorderInlineStartWidthPxAsync(unselectedRow);

        Assert.True(
            selectedBorderWidth > unselectedBorderWidth,
            $"""
            The selected canvas row's computed border-inline-start-width ({selectedBorderWidth}px)
            is not wider than the unselected row's ({unselectedBorderWidth}px) under
            forced-colors: active -- the structural cue CanvasNodeRow.razor.css's own
            @media (forced-colors: active) block adds for aria-selected="true" did not apply.
            """);

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design with a selected row under forced-colors: active");
    }

    /// <summary>
    /// The regression this proves: <c>[data-bf-theme="high-contrast"]</c> and the forced-colors
    /// <c>:root</c> block both re-declare <c>--bf-color-focus-ring</c>, and if the theme's own
    /// selector ever regains higher (or the forced-colors block ever regains lower) specificity
    /// than the other, a user who BOTH opts into the high-contrast theme AND has an OS-level
    /// forced-colors mode active loses the forced-colors hardening entirely — the token resolves
    /// to the theme's own <c>#0b3d91</c> instead of the system <c>Highlight</c> color forced-colors
    /// hardening exists to guarantee. No other scenario in this class applies both mechanisms to
    /// the same page at once.
    /// </summary>
    [Fact]
    public async Task TheOptInThemeDoesNotDefeatForcedColorsFocusRingHardening()
    {
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl);
        await ApplyForcedColorsAsync();
        await ApplyHighContrastThemeAsync();

        var focusRing = await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--bf-color-focus-ring').trim()");

        Assert.Equal("Highlight", focusRing);
    }

    private static async Task<double> BorderInlineStartWidthPxAsync(ILocator row)
    {
        var value = await row.EvaluateAsync<string>("el => getComputedStyle(el).borderInlineStartWidth").ConfigureAwait(false);
        return double.Parse(value.Replace("px", string.Empty, StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture);
    }
}
