using System.Diagnostics.CodeAnalysis;
using Microsoft.Playwright;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Scans every shipped-default surface at the 320-CSS-px-wide viewport WCAG 1.4.10 Reflow
/// requires (equivalent to 1280 CSS px at 400% zoom; 512 is just tall enough to keep this suite's
/// own scrolling-to-find-things down, not itself a criterion value), and separately proves WCAG
/// 1.4.12 Text Spacing at the same viewport — the two AA criteria the existing suites' fixed
/// 1280×720 default context can never exercise (docs/accessibility-statement-plan.md,
/// Increment A2/A3). Every scenario asserts no horizontal scroll before re-running the axe
/// assertion, since <c>target-size</c> and <c>color-contrast</c> results can differ once layout
/// reflows.
/// </summary>
public sealed class ReflowAccessibilityTests : E2ETestBase
{
    private const string WcagTextSpacingOverride = """
        * {
          line-height: 1.5em !important;
          letter-spacing: 0.12em !important;
          word-spacing: 0.16em !important;
        }
        p {
          margin-bottom: 2em !important;
        }
        """;

    [SuppressMessage(
        "Design",
        "CA1062:Validate arguments of public methods",
        Justification = "Both parameters are the collection fixtures xUnit itself supplies via ICollectionFixture<T> -- never null in practice -- and a base(...) initializer runs before any guard this body could add, so an in-body check here would be advisory only.")]
    public ReflowAccessibilityTests(SampleAppFixture sampleApp, BrowserFixture browserFixture)
        : base(sampleApp, browserFixture)
    {
    }

    /// <summary>
    /// The 320 CSS px width WCAG 1.4.10 Reflow mandates support for, on a per-test context
    /// rather than the 1280×720 every other suite's default context uses.
    /// </summary>
    private protected override BrowserNewContextOptions ContextOptions => new()
    {
        ViewportSize = new ViewportSize { Width = 320, Height = 512 },
    };

    [Fact]
    public async Task FillPageReflowsWithoutHorizontalScrollAndStaysAccessible()
    {
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl);

        await AssertNoHorizontalScrollAsync(Page, "/fill");
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/fill at 320×512");
    }

    [Fact]
    public async Task DesignPageReflowsWithoutHorizontalScrollAndStaysAccessible()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Text");

        await AssertNoHorizontalScrollAsync(Page, "/design");
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design at 320×512");
    }

    [Fact]
    public async Task LibraryPageReflowsWithoutHorizontalScrollAndStaysAccessible()
    {
        await Page.GotoAsync($"{BaseUrl}/library");
        await Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Benefits Enrollment" }).WaitForAsync();

        await AssertNoHorizontalScrollAsync(Page, "/library");
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/library at 320×512");
    }

    [Fact]
    public async Task SubmissionPageReflowsWithoutHorizontalScrollAndStaysAccessible()
    {
        await GotoSubmissionAsync();

        await AssertNoHorizontalScrollAsync(Page, "/submission/{id}");
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/submission/{id} at 320×512");
    }

    [Fact]
    public async Task FillPageSurvivesTheWcagTextSpacingOverrideWithoutClippingAndStaysAccessible()
    {
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl);
        await Page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = WcagTextSpacingOverride });

        await AssertNoContentClippingAsync(Page, "/fill", ".bf-field");
        await AssertNoHorizontalScrollAsync(Page, "/fill under the text-spacing override");
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/fill under the WCAG 1.4.12 text-spacing override");
    }

    [Fact]
    public async Task DesignPageSurvivesTheWcagTextSpacingOverrideWithoutClippingAndStaysAccessible()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Text");
        await Assertions.Expect(DesignerDriver.CanvasRows(Page)).ToHaveCountAsync(1);
        await Page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = WcagTextSpacingOverride });

        await AssertNoContentClippingAsync(Page, "/design", ".bf-canvas-row");
        await AssertNoHorizontalScrollAsync(Page, "/design under the text-spacing override");
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design under the WCAG 1.4.12 text-spacing override");
    }

    [Fact]
    public async Task LibraryPageSurvivesTheWcagTextSpacingOverrideWithoutClippingAndStaysAccessible()
    {
        await Page.GotoAsync($"{BaseUrl}/library");
        await Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Benefits Enrollment" }).WaitForAsync();
        await Page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = WcagTextSpacingOverride });

        await AssertNoContentClippingAsync(Page, "/library", ".bf-form-card");
        await AssertNoHorizontalScrollAsync(Page, "/library under the text-spacing override");
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/library under the WCAG 1.4.12 text-spacing override");
    }

    [Fact]
    public async Task SubmissionPageSurvivesTheWcagTextSpacingOverrideWithoutClippingAndStaysAccessible()
    {
        await GotoSubmissionAsync();
        await Page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = WcagTextSpacingOverride });

        await AssertNoContentClippingAsync(Page, "/submission/{id}", ".bf-submission__page");
        await AssertNoHorizontalScrollAsync(Page, "/submission/{id} under the text-spacing override");
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/submission/{id} under the WCAG 1.4.12 text-spacing override");
    }

    /// <summary>
    /// Pins the desktop three-column path right at the boundary the dock-collapse breakpoint
    /// introduces, not just that it existed before this increment's media query touched the
    /// grid at all. 976px (61rem) is comfortably above the 60rem (960px)
    /// <c>--bf-breakpoint-dock-collapse</c> token (blazeforms-designer.css) — the panes must
    /// still render side by side, in one row, immediately above the line where they stack.
    /// </summary>
    [Fact]
    public async Task DesignerPanesStayThreeColumnsJustAboveTheDockCollapseBreakpoint()
    {
        await Page.SetViewportSizeAsync(976, 700);
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Text");

        var palette = Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Field palette" });
        var canvas = Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Canvas" });
        var properties = Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Properties" });

        var paletteBox = await palette.BoundingBoxAsync();
        var canvasBox = await canvas.BoundingBoxAsync();
        var propertiesBox = await properties.BoundingBoxAsync();

        Assert.NotNull(paletteBox);
        Assert.NotNull(canvasBox);
        Assert.NotNull(propertiesBox);

        // Side by side means all three share (nearly) the same top offset, at increasing X --
        // stacked would instead put them at increasing Y and the same X.
        Assert.True(Math.Abs(paletteBox!.Y - canvasBox!.Y) < 2, "The palette and canvas panes are not on the same row just above the dock-collapse breakpoint.");
        Assert.True(Math.Abs(canvasBox.Y - propertiesBox!.Y) < 2, "The canvas and properties panes are not on the same row just above the dock-collapse breakpoint.");
        Assert.True(canvasBox.X > paletteBox.X, "The canvas pane is not to the right of the palette pane just above the dock-collapse breakpoint.");
        Assert.True(propertiesBox.X > canvasBox.X, "The properties pane is not to the right of the canvas pane just above the dock-collapse breakpoint.");
    }

    private async Task GotoSubmissionAsync()
    {
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl).ConfigureAwait(false);
        await SampleFormDriver.CompleteApplicantInformationPageAsync(Page).ConfigureAwait(false);
        await SampleFormDriver.CompleteCoverageSelectionPageAsync(Page).ConfigureAwait(false);
        await SampleFormDriver.CompleteReviewPageAsync(Page).ConfigureAwait(false);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit" }).ClickAsync().ConfigureAwait(false);
        await Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Submission received" }).WaitForAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// WCAG 1.4.10 Reflow's own operational definition: no content requires scrolling in the
    /// dimension perpendicular to the reading direction. Checks both the document itself and
    /// every element that manages its own horizontal overflow — a computed <c>overflow-x</c> of
    /// <c>auto</c>/<c>scroll</c> (<c>.bf-designer__pane-body</c>, <c>.bf-linter-dock__body</c>,
    /// <c>VersionHistory</c>'s own scroller), or <c>hidden</c>/<c>clip</c> that isn't this
    /// codebase's visually-hidden pattern (see <see cref="IsVisuallyHiddenPatternScript"/>) — a
    /// pane that scrolls internally, or a truncating element that clips instead of scrolling,
    /// never widens <c>document.documentElement.scrollWidth</c>, so checking only that would
    /// leave the exact reflow failure this increment exists to catch undetected. An element (or
    /// an ancestor) carrying <c>data-bf-overflow-expected</c> is skipped outright — the
    /// documented opt-out for a deliberately 2D-scrollable region 1.4.10 itself excepts (a wide
    /// data table, say). A one-pixel tolerance absorbs sub-pixel layout rounding without hiding a
    /// real overflow.
    /// </summary>
    private static async Task AssertNoHorizontalScrollAsync(IPage page, string scenario)
    {
        var result = await page.EvaluateAsync<HorizontalOverflowResult>(
            $$$"""
            () => {
              const tolerance = 1;
              const documentOverflows =
                document.documentElement.scrollWidth > document.documentElement.clientWidth + tolerance;

              {{{IsVisuallyHiddenPatternScript}}}
              {{{NativeFormControlsScript}}}

              const internalViolations = [];
              for (const el of document.querySelectorAll('*')) {
                // Same exclusion as the clipping sweep below, for the same reason: Chromium
                // computes overflow-x as 'clip' for <input>/<select>, not 'visible', so without
                // this the native single-line text scrolling every text input already does would
                // read as a 1.4.10 violation on every field with a value longer than its box.
                if (nativeFormControls.includes(el.tagName)) {
                  continue;
                }
                if (el.closest('[data-bf-overflow-expected]')) {
                  continue;
                }

                const style = getComputedStyle(el);
                const overflowX = style.overflowX;
                const managesOwnOverflow = overflowX === 'auto' || overflowX === 'scroll'
                  || ((overflowX === 'hidden' || overflowX === 'clip') && !isVisuallyHiddenPattern(el, style));
                if (!managesOwnOverflow) {
                  continue;
                }
                if (el.scrollWidth > el.clientWidth + tolerance) {
                  const cls = el.className ? '.' + String(el.className).trim().split(/\s+/).join('.') : '';
                  internalViolations.push(el.tagName.toLowerCase() + cls
                    + ' (scrollWidth ' + el.scrollWidth + ' > clientWidth ' + el.clientWidth + ')');
                }
              }

              return { documentOverflows, internalViolations };
            }
            """).ConfigureAwait(false);

        Assert.False(result.DocumentOverflows, $"{scenario} requires horizontal scrolling of the document at 320px wide (WCAG 1.4.10).");
        Assert.True(
            result.InternalViolations.Length == 0,
            $"""
            {scenario} has element(s) that manage their own horizontal scrolling but are
            currently overflowing it anyway at 320px wide (WCAG 1.4.10). If this is a
            deliberately 2D-scrollable region (a wide data table, say -- 1.4.10 itself excepts
            these), mark it with data-bf-overflow-expected to exempt it from this sweep:
            {string.Join(Environment.NewLine, result.InternalViolations)}
            """);
    }

    /// <summary>
    /// WCAG 1.4.12 Text Spacing's own failure mode: an element whose content overflows its own
    /// box once the spacing override is applied, rather than reflowing or scrolling within
    /// itself. Sweeps every element on the page rather than a hand-picked selector list — a
    /// cherry-picked list can only prove the elements on it happen to be safe, and every element
    /// this suite originally picked (labels, buttons, headings) sizes itself from its own content
    /// or from padding plus <c>min-block-size</c>, so none of them could ever fail regardless of
    /// what the CSS says.
    /// </summary>
    private static async Task AssertNoContentClippingAsync(IPage page, string scenario, string contentSelector)
    {
        var contentCount = await page.Locator(contentSelector).CountAsync().ConfigureAwait(false);
        Assert.True(
            contentCount > 0,
            $"{scenario}: the expected content selector '{contentSelector}' matched no elements -- the 1.4.12 sweep below would only have host chrome to check, which proves nothing about this page's own content.");

        var result = await page.EvaluateAsync<ClippingSweepResult>(
            $$$"""
            () => {
              const tolerance = 2;
              const violations = [];
              let candidateCount = 0;

              {{{IsVisuallyHiddenPatternScript}}}
              {{{NativeFormControlsScript}}}

              for (const el of document.querySelectorAll('*')) {
                if (el === document.documentElement || el === document.body) {
                  continue;
                }
                if (nativeFormControls.includes(el.tagName)) {
                  continue;
                }
                if (el.closest('[data-bf-overflow-expected]')) {
                  continue;
                }

                const style = getComputedStyle(el);
                const hiddenPattern = isVisuallyHiddenPattern(el, style);
                const overflowY = style.overflowY;
                const overflowX = style.overflowX;

                // auto/scroll always manage their own overflow, so they're excluded outright.
                // hidden/clip are excluded ONLY for this codebase's own visually-hidden pattern
                // (an intentionally tiny or clip-path'd box holding assistive-tech-only text,
                // never visible regardless of spacing) -- any other hidden/clip element that
                // truncates real content is exactly the 1.4.12 failure this sweep exists to
                // catch, and is flagged rather than silently exempted.
                const excludedY = overflowY === 'auto' || overflowY === 'scroll'
                  || ((overflowY === 'hidden' || overflowY === 'clip') && hiddenPattern);
                const excludedX = overflowX === 'auto' || overflowX === 'scroll'
                  || ((overflowX === 'hidden' || overflowX === 'clip') && hiddenPattern);
                const checkHeight = !excludedY;
                const checkWidth = !excludedX;
                if (!checkHeight && !checkWidth) {
                  continue;
                }

                candidateCount++;
                const cls = el.className ? '.' + String(el.className).trim().split(/\s+/).join('.') : '';
                const describe = el.tagName.toLowerCase() + cls;

                if (checkHeight && el.scrollHeight > el.clientHeight + tolerance) {
                  violations.push(describe + ' clips height (scrollHeight ' + el.scrollHeight
                    + ' > clientHeight ' + el.clientHeight + ')');
                }
                if (checkWidth && el.scrollWidth > el.clientWidth + tolerance) {
                  violations.push(describe + ' clips width (scrollWidth ' + el.scrollWidth
                    + ' > clientWidth ' + el.clientWidth + ')');
                }
              }

              return { candidateCount, violations };
            }
            """).ConfigureAwait(false);

        // Guards the guard, the same way ComponentCssTokenGuardTests.AtLeastOneRazorCssFileWasFound
        // does for its own glob: if every element on the page were somehow excluded (a markup
        // change that wraps everything in an overflow:auto container, say), the clipping
        // assertion below would pass vacuously and this would be the only signal that happened.
        // The contentCount assertion above is the guard that actually matters -- this one only
        // proves the sweep's loop ran at all, which host chrome alone already satisfies.
        Assert.True(result.CandidateCount > 0, $"{scenario}: no element was eligible for the 1.4.12 clipping sweep -- the check did not run.");

        Assert.True(
            result.Violations.Length == 0,
            $"""
            {scenario} has element(s) that clip their own content under the WCAG 1.4.12
            text-spacing override. If this is a legitimate out-of-flow descendant (a popover or
            tooltip positioned outside its parent's own box, say), mark it with
            data-bf-overflow-expected to exempt it from this sweep:
            {string.Join(Environment.NewLine, result.Violations)}
            """);
    }

    /// <summary>
    /// A browser-side JS function, spliced into both sweeps above, that recognizes this
    /// codebase's own visually-hidden pattern (<c>FormRenderer.razor.css</c>'s
    /// <c>.bf-visually-hidden</c>, <c>FormTable.razor.css</c>'s <c>.bf-form-table__caption</c>):
    /// an intentionally near-zero-size box, or one clipped via <c>clip</c>/<c>clip-path</c> —
    /// assistive-tech-only text that is never visible regardless of the text-spacing override, so
    /// 1.4.12 (which is about visible content) has nothing to say about it. Anything else with
    /// <c>overflow: hidden</c> or <c>clip</c> — the first truncating
    /// <c>text-overflow: ellipsis</c> element a future component adds, say — is a real candidate
    /// for both sweeps, not silently exempted by the overflow value alone.
    /// </summary>
    private const string IsVisuallyHiddenPatternScript = """
        const isVisuallyHiddenPattern = (el, style) => {
          const tinyBox = el.clientWidth <= 1 && el.clientHeight <= 1;
          const clipped = (style.clipPath && style.clipPath !== 'none')
            || (style.clip && style.clip !== 'auto');
          return tinyBox || clipped;
        };
        """;

    /// <summary>
    /// <c>&lt;input&gt;</c>/<c>&lt;textarea&gt;</c>/<c>&lt;select&gt;</c> excluded from both
    /// sweeps regardless of their own computed overflow -- Chromium reports <c>clip</c> for
    /// <c>&lt;input&gt;</c>/<c>&lt;select&gt;</c> and <c>auto</c> for <c>&lt;textarea&gt;</c>,
    /// neither of which is <c>visible</c>, but their own *value* text still scrolls or clips as a
    /// native UA behavior independent of the CSS overflow property both sweeps otherwise trust.
    /// That is not a WCAG 1.4.10/1.4.12 failure either sweep looks for, so the widget itself is
    /// excluded; the wrapping <c>.bf-field</c>/label markup around it is still swept normally.
    /// </summary>
    private const string NativeFormControlsScript = """
        const nativeFormControls = ['INPUT', 'TEXTAREA', 'SELECT'];
        """;

    /// <summary>
    /// A plain settable-property class, not a record with a positional constructor — Playwright's
    /// own <c>EvaluateAsync&lt;T&gt;</c> deserializes the browser-side JSON result via
    /// <see cref="Activator.CreateInstance(Type)"/> plus property assignment, which needs a
    /// public parameterless constructor a record's own primary constructor doesn't provide.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by Playwright's own JSON deserialization of the browser-side EvaluateAsync<T> result, not directly by this file's own code.")]
    private sealed class HorizontalOverflowResult
    {
        public bool DocumentOverflows { get; set; }

        public string[] InternalViolations { get; set; } = [];
    }

    /// <inheritdoc cref="HorizontalOverflowResult"/>
    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by Playwright's own JSON deserialization of the browser-side EvaluateAsync<T> result, not directly by this file's own code.")]
    private sealed class ClippingSweepResult
    {
        public int CandidateCount { get; set; }

        public string[] Violations { get; set; } = [];
    }
}
