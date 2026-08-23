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
    /// The barrier every caller closing a rule/calculation dialog (<c>VisibilityRuleEditor</c>,
    /// <c>CalculationEditor</c>) needs before its own NEXT interaction: waits for
    /// <paramref name="buttonName"/> (<c>PropertiesPanel</c>'s own "Add/Edit rule" or
    /// "Add/Edit calculation" action button — whichever text the node's post-commit state now
    /// shows) to actually hold real DOM focus.
    /// </summary>
    /// <remarks>
    /// <b>Why this barrier exists at all.</b> Closing either dialog only ARMS a one-shot
    /// <c>_focusVisibilityActionOnNextRender</c>/<c>_focusCalculationActionOnNextRender</c> flag on
    /// <c>PropertiesPanel</c> — the actual <c>ElementReference.FocusAsync()</c> JS interop call
    /// fires on that component's OWN next <c>OnAfterRenderAsync</c>, a render <c>Locator.WaitForAsync</c>
    /// on the DIALOG's own hidden state says nothing about (the dialog leaving the DOM and the
    /// panel's own focus-restore render are two independent things, not one atomic step). A
    /// caller that clicks something else (a different canvas row, say) in that window is racing
    /// this still-in-flight interop call: if it lands AFTER the caller's own click, it silently
    /// steals focus back onto this button, overwriting whatever the caller's click had just set —
    /// not a timing blip, a genuine wrong terminal focus state. This barrier fully closes THAT
    /// race — it is complete and correct for the dialog-close restore specifically. It is NOT the
    /// only focus-arming path in this area, though: selecting a DIFFERENT node afterward (a plain
    /// click, <c>DesignerCanvas.Activate</c>) separately arms <c>PropertiesPanel</c>'s own
    /// "new node, no other focus intent" steal to its Label input
    /// (<c>PropertiesPanelTests.SelectingANewNodeWithNoFocusIntentMovesFocusToTheLabelInput</c>) —
    /// a second, independent race this barrier says nothing about. A caller needing the CANVAS
    /// (not the properties panel) to hold focus after selecting a different row must route around
    /// THAT one too — see <see cref="MoveRovingCursorToRowAsync"/> and
    /// <see cref="SelectCanvasRowAndWaitForLabelFocusAsync"/> for the two ways this driver closes
    /// it, and their own remarks for which race each one actually guards against — an earlier
    /// version of this comment attributed a test failure to just this barrier's own race, which
    /// was at best incomplete: the Label-steal explains the same symptom at least as well.
    /// </remarks>
    public static async Task WaitForDialogCloseFocusRestoreAsync(IPage page, string buttonName)
    {
        var button = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = buttonName });
        Assert.True(
            await WaitForFocusAsync(button).ConfigureAwait(false),
            $"Focus never returned to the '{buttonName}' button after the dialog that opened over it closed -- {nameof(WaitForDialogCloseFocusRestoreAsync)}'s own remarks name the race this guards against.");
    }

    /// <summary>
    /// Fills the currently-selected field's own "Label" input via the properties panel and
    /// commits it on blur (<c>Tab</c>) — the same label-then-tab pattern
    /// <c>DesignAccessibilityTests</c> already uses inline, lifted here once a second caller
    /// (<c>DesignerDialogAccessibilityTests</c>) needs it to give two otherwise-identical
    /// "Untitled Text" rows distinct, clickable labels. <c>Exact = true</c>: Playwright's
    /// <c>GetByLabel</c> is a case-insensitive substring match by default, and "Label" is itself
    /// a substring of the repeating group's own "Item label" input
    /// (<c>DesignerStrings.PropItemLabelLabel</c>) — an inexact match would throw Playwright's own
    /// strict-mode error the first time this ran against a <c>Repeating</c> node's properties.
    /// </summary>
    public static async Task SetLabelAsync(IPage page, string label)
    {
        await page.GetByLabel("Label", new PageGetByLabelOptions { Exact = true }).FillAsync(label).ConfigureAwait(false);
        await page.Keyboard.PressAsync("Tab").ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a field from the palette and, once (and only once) real DOM focus has actually landed
    /// on its new canvas row — the same round trip <see cref="AddFieldFromPaletteAsync"/>'s own
    /// NewNode focus intent needs a render for — labels it via <see cref="SetLabelAsync"/>.
    /// </summary>
    /// <remarks>
    /// <b>The race this guards against is real, confirmed directly, and specifically a
    /// stale-element race, not a missing-element one.</b> Playwright's own locator resolution
    /// already auto-retries until a target exists, so a bare <c>GetByLabel("Label").FillAsync(...)</c>
    /// right after a FIRST palette add is safe on its own — there is no pre-existing "Label" input
    /// for it to resolve to early. The race bites on a SECOND (or later) add: the properties panel
    /// is already showing the PRIOR field's own "Label" input at the moment the click lands, so
    /// <c>GetByLabel</c> resolves to that already-present, already-actionable element immediately
    /// — before the round trip that swaps the panel over to the new field has come back — and fills
    /// the wrong one. Observed directly this way: two sequential adds-then-label calls with no wait
    /// relabeled the FIRST field twice, leaving the second one's own default "Untitled …" label
    /// untouched. Waiting for the new row to actually hold DOM focus (<see cref="WaitForFocusAsync"/>)
    /// closes that window. Every caller doing a SECOND-OR-LATER palette add followed immediately
    /// by a label fill, with no such wait in between, is exposed to this same race.
    /// </remarks>
    public static async Task<ILocator> AddLabeledFieldFromPaletteAsync(IPage page, string nodeTypeLabel, string fieldLabel)
    {
        var rowCountBefore = await CanvasRows(page).CountAsync().ConfigureAwait(false);
        await AddFieldFromPaletteAsync(page, nodeTypeLabel).ConfigureAwait(false);

        var newRow = CanvasRows(page).Nth(rowCountBefore);
        await Assertions.Expect(CanvasRows(page)).ToHaveCountAsync(rowCountBefore + 1).ConfigureAwait(false);
        Assert.True(await WaitForFocusAsync(newRow).ConfigureAwait(false), $"The new '{nodeTypeLabel}' row never took real DOM focus after being added from the palette.");

        await SetLabelAsync(page, fieldLabel).ConfigureAwait(false);
        return newRow;
    }

    /// <summary>
    /// The single canvas row whose own <c>.bf-canvas-row__label</c> text is exactly
    /// <paramref name="rowLabel"/> — filtered on the label span itself, not the whole row's
    /// rendered text, since a row naming another field in its own visibility/calc summary (e.g.
    /// "Shown when Field A is …") would otherwise also match a filter against
    /// <paramref name="rowLabel"/>.
    /// </summary>
    public static ILocator CanvasRowByLabel(IPage page, string rowLabel) =>
        page.Locator(".bf-canvas-row")
            .Filter(new LocatorFilterOptions { Has = page.Locator(".bf-canvas-row__label", new PageLocatorOptions { HasTextString = rowLabel }) });

    /// <summary>
    /// Selects (and activates) <see cref="CanvasRowByLabel"/> via a plain click —
    /// <c>CanvasNodeRow</c>'s own <c>Activate</c> handler, the same one a pointer user relies on,
    /// rather than this driver's own keyboard-roving helpers, for a caller that just needs a
    /// specific row selected again after some other row's own action (an applied rule, say)
    /// moved the roving cursor elsewhere.
    /// </summary>
    public static Task SelectCanvasRowAsync(IPage page, string rowLabel) =>
        CanvasRowByLabel(page, rowLabel).ClickAsync();

    /// <summary>
    /// Moves the roving cursor — never the committed selection — from whichever row currently
    /// holds real DOM focus to the row labeled <paramref name="targetLabel"/>, pressing
    /// <paramref name="key"/> (<c>ArrowUp</c>/<c>ArrowDown</c>) up to <paramref name="maxSteps"/>
    /// times, and asserts focus actually lands there.
    /// </summary>
    /// <remarks>
    /// <c>DesignerCanvas.SetActive</c> (the ↑/↓/Home/End path) only ever sets
    /// <c>_activeNodeId</c>/<c>_pendingFocusNodeId</c> — it never touches
    /// <c>DesignerEditContext.Selection</c> at all, so unlike a click (<c>Activate</c>, which
    /// always commits <c>DesignerFocusIntent.None</c>) this can never arm
    /// <c>PropertiesPanel</c>'s own focus-steal to its Label input
    /// (<c>PropertiesPanelTests.SelectingANewNodeWithNoFocusIntentMovesFocusToTheLabelInput</c>).
    /// That is exactly why this is the right tool whenever the caller's own NEXT action needs the
    /// CANVAS itself to hold focus — <c>Delete</c>, <c>Alt+↑/↓</c>, <c>Ctrl+M</c> are all
    /// <c>_activeNodeId</c>-driven and never need the committed selection to have moved at all.
    /// Requires real DOM focus to already be SOMEWHERE inside the canvas before calling (arrow
    /// keys dispatch to <c>DesignerCanvas.OnKeyDown</c>, bound on the canvas's own root element —
    /// they do nothing if focus is elsewhere, a properties-panel button, say); re-clicking the
    /// row <see cref="DesignerEditContext.Selection"/> already names first
    /// (<c>PropertiesPanelTests.ReselectingTheSameNodeDoesNotRefocus</c> — a same-node reselect
    /// arms nothing) is the safe way to get there from outside the canvas.
    /// </remarks>
    public static async Task MoveRovingCursorToRowAsync(IPage page, string targetLabel, string key, int maxSteps = 10)
    {
        var target = CanvasRowByLabel(page, targetLabel);

        for (var step = 0; step < maxSteps; step++)
        {
            if (await IsFocusedAsync(target).ConfigureAwait(false))
            {
                return;
            }

            await page.Keyboard.PressAsync(key).ConfigureAwait(false);
        }

        Assert.True(
            await WaitForFocusAsync(target).ConfigureAwait(false),
            $"The canvas row labeled '{targetLabel}' never took real DOM focus after up to {maxSteps} '{key}' press(es).");
    }

    /// <summary>
    /// Selects <paramref name="rowLabel"/> via a plain click and waits for real DOM focus to land
    /// on the properties panel's own "Label" input — the product's own deliberate, unit-tested
    /// terminal state for selecting a DIFFERENT node with no other focus intent
    /// (<c>PropertiesPanelTests.SelectingANewNodeWithNoFocusIntentMovesFocusToTheLabelInput</c>),
    /// asserted as the barrier rather than raced against. Leaves <c>EditContext.Selection</c>
    /// (and therefore the properties panel's own rendered content) pointed at
    /// <paramref name="rowLabel"/>'s node — the right tool when the caller's own NEXT action is a
    /// properties-panel control that needs to be operating on THIS node, not the canvas itself
    /// (a caller needing the canvas to hold focus afterward should use
    /// <see cref="MoveRovingCursorToRowAsync"/> instead, never this).
    /// </summary>
    public static async Task SelectCanvasRowAndWaitForLabelFocusAsync(IPage page, string rowLabel)
    {
        await SelectCanvasRowAsync(page, rowLabel).ConfigureAwait(false);

        var labelInput = page.GetByLabel("Label", new PageGetByLabelOptions { Exact = true });
        Assert.True(
            await WaitForFocusAsync(labelInput).ConfigureAwait(false),
            $"Focus never landed on the Label input after selecting '{rowLabel}' -- PropertiesPanel's own deliberate focus-steal to a newly selected node's Label input never fired.");
    }

    /// <summary>
    /// Drives the toolbar's own publish flow to completion: opens <c>PublishDialog</c>, types
    /// <paramref name="changeNote"/> into the change-note field (the dialog only shows this field
    /// once no blocking lint issue remains, which every caller of this helper has already
    /// guaranteed), confirms, and waits for the sample host's own post-publish navigation to
    /// <c>/library</c> (<c>Design.razor</c>'s own <c>HandlePublishedAsync</c>).
    /// </summary>
    public static async Task PublishAsync(IPage page, string changeNote)
    {
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Publish", Exact = true }).ClickAsync().ConfigureAwait(false);

        var dialog = page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Publish this form" });
        await dialog.WaitForAsync().ConfigureAwait(false);

        await dialog.GetByLabel("What changed?").FillAsync(changeNote).ConfigureAwait(false);
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Publish" }).ClickAsync().ConfigureAwait(false);

        // Both Library.razor's own document <h1> and FormLibrary's own <h2> title carry this
        // exact text (PRD's host-owns-the-h1 contract), so this waits on the level-1 heading
        // specifically rather than a bare role match, which would resolve to both and fail
        // Playwright's strict mode.
        await page.Locator("h1").GetByText("Form library").WaitForAsync().ConfigureAwait(false);
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
