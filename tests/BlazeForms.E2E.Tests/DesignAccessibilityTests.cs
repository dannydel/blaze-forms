using System.Diagnostics.CodeAnalysis;
using Microsoft.Playwright;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Axe accessibility scans against <c>/design</c>, the shipped-default designer shell (PRD §4.1,
/// §11, §14 #3; AGENTS.md invariant #4) — the designer half of the "Playwright + axe report zero
/// WCAG 2.2 AA violations" success criterion the renderer's own <see cref="FillAccessibilityTests"/>
/// and <see cref="SubmissionAccessibilityTests"/> already gate. Every scenario opens a fresh,
/// never-before-seen form id (<see cref="DesignerDriver.GotoNewDesignAsync"/>), so no two tests —
/// nor a test and a later run of itself — ever share one draft on the collection-scoped sample
/// host.
/// </summary>
public sealed class DesignAccessibilityTests : E2ETestBase
{
    [SuppressMessage(
        "Design",
        "CA1062:Validate arguments of public methods",
        Justification = "Both parameters are the collection fixtures xUnit itself supplies via ICollectionFixture<T> -- never null in practice -- and a base(...) initializer runs before any guard this body could add, so an in-body check here would be advisory only.")]
    public DesignAccessibilityTests(SampleAppFixture sampleApp, BrowserFixture browserFixture)
        : base(sampleApp, browserFixture)
    {
    }

    [Fact]
    public async Task InitialRenderOfTheDesignPageHasNoAccessibilityViolations()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);

        // The three docked panes plus the toolbar and linter dock are the shipped-default shell
        // (PRD §4.1, D9) -- present even before any page exists.
        await Assertions.Expect(Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Field palette" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Canvas" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Properties" })).ToBeVisibleAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design initial render (three panes + palette)");
    }

    [Fact]
    public async Task AddingAFieldFromThePaletteHasNoAccessibilityViolations()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Text");

        await Assertions.Expect(DesignerDriver.CanvasRows(Page)).ToHaveCountAsync(1);
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design with one field added from the palette");
    }

    [Fact]
    public async Task RovingFocusCanvasReachesRowsMovesTheCursorOpensPropertiesOnEnterAndDoesNotTrapTab()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Text");
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Email");

        var rows = DesignerDriver.CanvasRows(Page);
        await Assertions.Expect(rows).ToHaveCountAsync(2);

        // Move focus away from wherever the last palette add left it, then prove Tab alone
        // reaches the canvas's single roving-tabindex row from outside it -- the Email row, added
        // last.
        await Page.GetByLabel("Search fields").FocusAsync();
        Assert.True(await DesignerDriver.TabUntilFocusedAsync(Page, rows.Nth(1)));

        var fieldIdInput = Page.Locator("input.bf-props__input--readonly");
        var selectedIdBeforeEnter = await fieldIdInput.InputValueAsync();

        // ArrowUp moves the roving cursor (and real DOM focus) to the Text row without touching
        // the committed selection -- the properties panel must still show Email's own properties.
        await Page.Keyboard.PressAsync("ArrowUp");
        Assert.True(await DesignerDriver.WaitForFocusAsync(rows.Nth(0)));
        Assert.Equal(selectedIdBeforeEnter, await fieldIdInput.InputValueAsync());

        // Enter commits the Text row as the selection -- the properties panel now shows a
        // different field's own Field ID.
        await Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(fieldIdInput).Not.ToHaveValueAsync(selectedIdBeforeEnter);

        // No keyboard trap: Tab must leave the canvas's own single tab stop.
        await Page.Keyboard.PressAsync("Tab");
        Assert.False(await DesignerDriver.IsFocusedAsync(rows.Nth(0)));

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design after roving-focus canvas keyboard navigation");
    }

    [Fact]
    public async Task EveryReorderPathMovesTheNodeAndTheMoveDialogStaysAccessible()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Text");
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Email");
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Phone");

        var rows = DesignerDriver.CanvasRows(Page);
        var rowLabels = Page.Locator(".bf-canvas-row__label");
        await Assertions.Expect(rows).ToHaveCountAsync(3);
        await Assertions.Expect(rowLabels).ToHaveTextAsync(["Untitled Text", "Untitled Email", "Untitled Phone"]);

        // Path 1: Alt+ArrowUp moves the active node (Phone, added last, still focused) one place
        // earlier within its own section. ToHaveTextAsync -- not a bare read-then-compare -- is
        // what makes this robust against the round trip a Blazor Server keydown handler needs to
        // actually land before the DOM reflects the reorder.
        Assert.True(await DesignerDriver.WaitForFocusAsync(rows.Nth(2)));
        await Page.Keyboard.PressAsync("Alt+ArrowUp");
        await Assertions.Expect(rowLabels).ToHaveTextAsync(["Untitled Text", "Untitled Phone", "Untitled Email"]);

        // Path 2: Ctrl+M opens the move-to-position dialog for the active node (still Phone -- a
        // move never changes which node the roving cursor sits on).
        await Page.Keyboard.PressAsync("Control+M");
        var moveDialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Move 'Untitled Phone'" });
        await moveDialog.WaitForAsync();
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design move-to-position dialog open");

        await moveDialog.GetByLabel("Position").SelectOptionAsync(new SelectOptionValue { Label = "Position 1" });
        await moveDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Move" }).ClickAsync();
        await moveDialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await Assertions.Expect(rowLabels).ToHaveTextAsync(["Untitled Phone", "Untitled Text", "Untitled Email"]);

        // Path 3: drag-and-drop -- pointer sugar, never the accessible contract, so this is a
        // functional smoke test only (this type's own remarks), not gated on its own axe scan.
        await DesignerDriver.DragRowOntoAsync(Page, sourceLabel: "Untitled Email", targetLabel: "Untitled Text");
        await Assertions.Expect(rowLabels).ToHaveTextAsync(["Untitled Phone", "Untitled Email", "Untitled Text"]);
    }

    [Fact]
    public async Task OpeningTheVisibilityRuleEditorHasNoAccessibilityViolations()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Text");
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Email");

        // Email is the currently-selected field (added last); its properties panel offers "Add
        // rule" since it carries no visibility rule yet.
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add rule" }).ClickAsync();

        var dialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Edit visibility rule for 'Untitled Email'" });
        await dialog.WaitForAsync();

        // A real condition row (referencing the other field, Text, never itself, since Text was
        // added first and so is _fields[0], AddCondition's own fallback) exercises the
        // field/operator/value selects the empty dialog alone would not.
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add condition" }).ClickAsync();
        await Assertions.Expect(dialog.GetByLabel("Condition 1 field")).ToBeVisibleAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design visibility rule editor open with one condition");
    }

    /// <summary>
    /// Opens <c>CalculationEditor</c> for a fresh calc node and authors a whole calculation —
    /// Operation, Format, one operand, and Apply — using only the keyboard, scanning the dialog
    /// itself while it is still open (<c>calc-engine-plan.md</c>, Increment C).
    /// </summary>
    [Fact]
    public async Task AuthoringACalculationKeyboardOnlyHasNoAccessibilityViolations()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Number");
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Calculated value");

        // "Calculated value" is the currently-selected field (added last); its properties panel
        // offers "Add calculation" since it carries no calculation yet.
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add calculation" }).ClickAsync();

        var dialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Edit calculation for 'Untitled Calculated value'" });
        await dialog.WaitForAsync();

        // The dialog opens with real DOM focus already on the Operation select (its own first
        // control) -- Tab twice reaches "Add operand" (past Operation, then Format) with no click.
        Assert.True(await DesignerDriver.IsFocusedAsync(dialog.GetByLabel("Operation")));
        await Page.Keyboard.PressAsync("Tab");
        await Page.Keyboard.PressAsync("Tab");
        Assert.True(await DesignerDriver.IsFocusedAsync(dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add operand" })));
        await Page.Keyboard.PressAsync("Enter");

        // The freshly added operand row defaults to Field kind against the only numeric
        // candidate ("Untitled Number") -- a real operand row exercises the Kind/Field selects
        // the empty dialog alone would not.
        await Assertions.Expect(dialog.GetByLabel("Operand 1 field")).ToBeVisibleAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design calculation editor open with one operand");

        // Shift+Tab back to Apply and press Enter -- commits the whole calculation via keyboard.
        var applyButton = dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Apply" });
        Assert.True(await DesignerDriver.TabUntilFocusedAsync(Page, applyButton, forward: false));
        await Page.Keyboard.PressAsync("Enter");

        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await Assertions.Expect(Page.GetByText("Sum of Untitled Number.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task LinterDockJumpToNodeMovesFocusToTheOffendingRow()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Text");

        // The fresh field carries no label -- A11Y-01, blocking -- so the dock (expanded by
        // default) shows exactly one finding with a jump-to-node action.
        await Page.GetByText("Linter (1)").WaitForAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Jump to 'Untitled Text'" }).ClickAsync();

        Assert.True(await DesignerDriver.WaitForFocusAsync(DesignerDriver.CanvasRows(Page).First));

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design linter dock open with a jump-to-node action");
    }

    [Fact]
    public async Task PublishDialogWithABlockingIssueDisablesConfirmAndListsTheBlocker()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Text");

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Publish", Exact = true }).ClickAsync();

        var dialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Publish this form" });
        await dialog.WaitForAsync();

        await Assertions.Expect(dialog.GetByText("Fix these issues before publishing:")).ToBeVisibleAsync();
        await Assertions.Expect(dialog.GetByText("This field has no label.")).ToBeVisibleAsync();

        var confirmButton = dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Publish" });
        await Assertions.Expect(confirmButton).ToBeDisabledAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design publish dialog with a blocking issue");
    }

    [Fact]
    public async Task EnteringAndExitingPreviewHasNoAccessibilityViolations()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Text");

        // Give the field a label before entering preview -- an unlabeled input is a real
        // accessibility defect in whatever form is being previewed (the linter's own blocking
        // A11Y-01), not in the preview surface itself, so this scenario labels the field first to
        // scan the shipped-default chrome rather than deliberately inaccessible test content.
        await Page.GetByLabel("Label").FillAsync("Full legal name");
        await Page.Keyboard.PressAsync("Tab");

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Preview" }).ClickAsync();
        await Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Preview" }).WaitForAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design preview pane open");

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Exit preview" }).ClickAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Preview" })).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// PRD §14 #2's own success criterion: a form author builds and publishes a form without
    /// touching a mouse. This drives the whole add-a-field -&gt; fix its one blocking lint issue
    /// -&gt; publish path via <see cref="IKeyboard"/> alone -- every navigation between controls is
    /// <c>Tab</c>/<c>Shift+Tab</c> (never <c>ILocator.ClickAsync</c>), every activation is
    /// <c>Enter</c>, and the change note and field label are typed. The one step this suite does
    /// not attempt keyboard-only is opening the session itself (<c>Page.GotoAsync</c>) and the
    /// final library-card assertion below, neither of which is part of the author's own
    /// build-and-publish flow PRD §14 #2 is about.
    /// </summary>
    [Fact]
    public async Task AuthorCanFixABlockingIssueAndPublishUsingOnlyTheKeyboard()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);

        // Tab forward to "Add page" and press Enter -- adds an empty page (PageTabStrip is the
        // very first focusable control after the toolbar and the whole field palette).
        var addPageButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add page" });
        Assert.True(await DesignerDriver.TabUntilFocusedAsync(Page, addPageButton));
        await Page.Keyboard.PressAsync("Enter");

        // Shift+Tab back to the palette's "Text" entry and press Enter. OnPaletteAddRequested
        // auto-creates the page's first section, adds an unlabeled Text field to it, and -- its
        // own NewNode focus intent -- moves real DOM focus straight to that field's new canvas
        // row, with no further navigation needed to reach it.
        var textPaletteButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Text", Exact = true });
        Assert.True(await DesignerDriver.TabUntilFocusedAsync(Page, textPaletteButton, forward: false));
        await Page.Keyboard.PressAsync("Enter");

        var row = DesignerDriver.CanvasRows(Page).First;
        Assert.True(await DesignerDriver.WaitForFocusAsync(row));

        // Tab twice -- past the read-only Field ID input -- to the Label input, type a label, and
        // Tab away to commit it on blur. This is the one blocking issue the fresh field carries
        // (A11Y-01: "This field has no label.").
        var labelInput = Page.GetByLabel("Label");
        Assert.True(await DesignerDriver.TabUntilFocusedAsync(Page, labelInput, maxSteps: 5));
        await Page.Keyboard.TypeAsync("Full legal name");
        await Page.Keyboard.PressAsync("Tab");

        // Shift+Tab back to the toolbar's Publish button and press Enter.
        var toolbarPublishButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Publish", Exact = true });
        Assert.True(await DesignerDriver.TabUntilFocusedAsync(Page, toolbarPublishButton, forward: false));
        await Page.Keyboard.PressAsync("Enter");

        var dialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Publish this form" });
        await dialog.WaitForAsync();

        // The label committed above already cleared the only blocking issue, so the dialog shows
        // the change-note field instead of a blocker list. Confirm starts disabled (no note yet)
        // and so out of the natural tab order -- Shift+Tab from the focus trap's own initial
        // Cancel button reaches the note textarea directly.
        var noteField = dialog.GetByLabel("What changed?");
        Assert.True(await DesignerDriver.TabUntilFocusedAsync(Page, noteField, forward: false));
        await Page.Keyboard.TypeAsync("Added a name field.");

        var confirmButton = dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Publish" });
        await Assertions.Expect(confirmButton).ToBeEnabledAsync();
        Assert.True(await DesignerDriver.TabUntilFocusedAsync(Page, confirmButton));
        await Page.Keyboard.PressAsync("Enter");

        // A successful publish raises FormDesigner.OnPublished, which the sample host's own
        // Design.razor turns into an immediate navigation to /library (Program.cs's own wiring) --
        // so proving the round trip completed means waiting for that page, not for this now-torn-
        // down dialog's own focus-restore (FormDesigner's toolbar button regains focus only on the
        // reloaded draft it never gets to show here, since the navigation pre-empts it). The
        // published card below is that independent, click-free proof: the version this session
        // just published is now visible there as version 1, Published. This test is the only one
        // in the whole suite that ever confirms a publish, so the card is unambiguous.
        var publishedCard = Page.Locator("article.bf-form-card")
            .Filter(new LocatorFilterOptions { HasTextString = "Untitled form" })
            .Filter(new LocatorFilterOptions { HasTextString = "Published" });
        await Assertions.Expect(publishedCard).ToHaveCountAsync(1);
    }
}
