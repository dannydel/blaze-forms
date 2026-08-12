using Microsoft.Playwright;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Drives <c>FormDesigner</c> (mounted at <c>/design/{formId}</c> by the sample host) through
/// accessible locators only — labels and roles, never a raw CSS id — mirroring
/// <see cref="SampleFormDriver"/>'s own contract for the renderer.
/// </summary>
internal static class DesignerDriver
{
    /// <summary>
    /// Navigates to a fresh <c>/design/{formId}</c> session with a newly minted, never-before-seen
    /// form id, so no two calls across this whole suite ever share one draft on the
    /// collection-scoped sample host — the same isolation <see cref="SampleFormDriver.GotoFillAsync"/>
    /// gives its own respondent key. The sample host's <c>Design.razor</c> forwards this id straight
    /// to <c>FormDesigner</c>, which builds a blank "Untitled form" draft in memory on the miss (no
    /// pages yet) rather than erroring — exactly the shipped-default empty state this suite's
    /// initial-render scenario exercises.
    /// </summary>
    /// <returns>The freshly minted form id this session opened onto, for a caller that needs it again.</returns>
    public static async Task<string> GotoNewDesignAsync(IPage page, string baseUrl)
    {
        var formId = $"design-e2e-{Guid.NewGuid():n}";
        await page.GotoAsync($"{baseUrl}/design/{formId}").ConfigureAwait(false);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add page" }).WaitForAsync().ConfigureAwait(false);
        return formId;
    }

    /// <summary>
    /// Adds a blank page via the page-tab strip's own "Add page" button — the prerequisite every
    /// other mutation in this driver needs, since a fresh design session opens with none. Leaves
    /// the new page with no section yet; a palette add (<see cref="AddFieldFromPaletteAsync"/>)
    /// creates one automatically on its own first call (<c>FormDesigner.OnPaletteAddRequested</c>'s
    /// own remarks), so callers never need this driver's own section-adding affordance separately.
    /// </summary>
    public static Task AddPageAsync(IPage page) =>
        page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add page" }).ClickAsync();

    /// <summary>
    /// Adds a field of the given palette entry's own display name (e.g. <c>"Text"</c>,
    /// <c>"Email"</c>) via the field palette — <see cref="PageGetByRoleOptions.Exact"/> so, say,
    /// <c>"Text"</c> never also matches <c>"Text area"</c>'s own button.
    /// </summary>
    public static Task AddFieldFromPaletteAsync(IPage page, string nodeTypeLabel) =>
        page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = nodeTypeLabel, Exact = true }).ClickAsync();

    /// <summary>
    /// Every canvas row currently rendered, in DOM (i.e. section/node) order — the WAI-ARIA
    /// grouped-listbox <c>role="option"</c> elements <c>DesignerCanvas</c> renders one per node.
    /// </summary>
    public static ILocator CanvasRows(IPage page) => page.GetByRole(AriaRole.Option);

    /// <summary>
    /// Tabs (<paramref name="forward"/>) or Shift+Tabs (backward) up to <paramref name="maxSteps"/>
    /// times until <paramref name="target"/> itself holds real DOM focus, checking before ever
    /// pressing a key in case it already does. Used instead of clicking whenever a test needs to
    /// prove a control is reachable by keyboard alone (PRD §14 #2) — most of this suite's own
    /// setup steps use an ordinary <c>ClickAsync</c> instead, since only the keyboard-only-publish
    /// scenario itself needs to prove the whole path is reachable without a pointer.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> once <paramref name="target"/> holds focus, or <see langword="false"/>
    /// if <paramref name="maxSteps"/> presses never reached it.
    /// </returns>
    public static async Task<bool> TabUntilFocusedAsync(IPage page, ILocator target, bool forward = true, int maxSteps = 60)
    {
        for (var step = 0; step < maxSteps; step++)
        {
            if (await IsFocusedAsync(target).ConfigureAwait(false))
            {
                return true;
            }

            await page.Keyboard.PressAsync(forward ? "Tab" : "Shift+Tab").ConfigureAwait(false);
        }

        return await IsFocusedAsync(target).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether <paramref name="target"/>'s own single matching element currently holds real DOM
    /// focus (<c>document.activeElement</c>) — the same check <see cref="TabUntilFocusedAsync"/>
    /// uses internally, exposed for a caller that has already tabbed there some other way (e.g. a
    /// mutation's own post-edit focus move) and just needs to assert it landed.
    /// </summary>
    public static Task<bool> IsFocusedAsync(ILocator target) =>
        target.EvaluateAsync<bool>("el => el === document.activeElement");

    /// <summary>
    /// Polls (no key presses of its own) for up to <paramref name="timeoutMs"/> until
    /// <paramref name="target"/> holds real DOM focus. Used instead of a bare
    /// <see cref="IsFocusedAsync"/> check whenever the focus being asserted is the tail of a
    /// just-issued mutation's own post-render <c>FocusAsync</c> call (e.g. a palette add's NewNode
    /// focus intent) — that JS interop round trip lands slightly after the DOM patch that grows the
    /// element count already has, so asserting the very instant a count-based wait resolves can
    /// otherwise race it.
    /// </summary>
    public static async Task<bool> WaitForFocusAsync(ILocator target, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (await IsFocusedAsync(target).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        return await IsFocusedAsync(target).ConfigureAwait(false);
    }

    /// <summary>
    /// A drag-and-drop reorder smoke test: dispatches the exact sequence of native
    /// <c>dragstart</c>/<c>dragover</c>/<c>drop</c>/<c>dragend</c> events <c>DesignerCanvas</c>'s
    /// own drag handlers listen for, moving whichever row's label matches
    /// <paramref name="sourceLabel"/> to immediately before the row matching
    /// <paramref name="targetLabel"/> — the same semantics <c>DesignerCanvas.DropOnRow</c>
    /// documents for a real pointer drag. Playwright's own <c>ILocator.DragToAsync</c> drives a
    /// drag through synthesized mouse movement, which is unreliable against a headless Chromium
    /// HTML5 drag source; dispatching the native events directly is the deterministic alternative
    /// every Chromium-based headless DnD test in the wild uses instead. Drag-and-drop is pointer
    /// sugar only, never this project's accessible contract (the keyboard reorder paths are), so
    /// this is a functional smoke test, not something this suite gates an axe scan on.
    /// </summary>
    public static async Task DragRowOntoAsync(IPage page, string sourceLabel, string targetLabel)
    {
        var source = page.Locator(".bf-canvas-row").Filter(new LocatorFilterOptions { HasTextString = sourceLabel });
        var target = page.Locator(".bf-canvas-row").Filter(new LocatorFilterOptions { HasTextString = targetLabel });

        await source.EvaluateAsync(
            "el => { window.__bfDrag = new DataTransfer(); el.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer: window.__bfDrag })); }")
            .ConfigureAwait(false);
        await target.EvaluateAsync(
            "el => { el.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: window.__bfDrag })); el.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: window.__bfDrag })); }")
            .ConfigureAwait(false);
        await source.EvaluateAsync(
            "el => el.dispatchEvent(new DragEvent('dragend', { bubbles: true, cancelable: true, dataTransfer: window.__bfDrag }))")
            .ConfigureAwait(false);
    }
}
