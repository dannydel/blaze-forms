using System.Diagnostics.CodeAnalysis;
using Microsoft.Playwright;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Axe accessibility scans against the six designer dialogs/panels no scenario in
/// <see cref="DesignAccessibilityTests"/> ever opens — <c>DeleteProtectionDialog</c>,
/// <c>RetireConfirmationDialog</c>, <c>VersionHistory</c>, <c>KeyboardHelpDialog</c>,
/// <c>ValidationRuleEditor</c> (with a real rule and condition), and <c>OptionsEditor</c> (with
/// option rows) — plus the cycle-detection <c>role="alert"</c> paths in both
/// <c>VisibilityRuleEditor</c> and <c>CalculationEditor</c> (docs/accessibility-statement-plan.md,
/// Increment B1). Every scenario opens a fresh, never-before-seen form id
/// (<see cref="DesignerDriver.GotoNewDesignAsync"/>), the same isolation
/// <see cref="DesignAccessibilityTests"/> already relies on.
/// </summary>
public sealed class DesignerDialogAccessibilityTests : E2ETestBase
{
    [SuppressMessage(
        "Design",
        "CA1062:Validate arguments of public methods",
        Justification = "Both parameters are the collection fixtures xUnit itself supplies via ICollectionFixture<T> -- never null in practice -- and a base(...) initializer runs before any guard this body could add, so an in-body check here would be advisory only.")]
    public DesignerDialogAccessibilityTests(SampleAppFixture sampleApp, BrowserFixture browserFixture)
        : base(sampleApp, browserFixture)
    {
    }

    [Fact]
    public async Task KeyboardHelpDialogHasNoAccessibilityViolations()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Keyboard shortcuts" }).ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Keyboard shortcuts" });
        await dialog.WaitForAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design keyboard shortcuts dialog open");
    }

    [Fact]
    public async Task VersionHistoryHasNoAccessibilityViolations()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddLabeledFieldFromPaletteAsync(Page, "Text", "Full legal name");

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Version history" }).ClickAsync();
        var region = Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Version history" });
        await region.WaitForAsync();

        // Never published yet -- the empty-state message, not the version table.
        await Assertions.Expect(Page.GetByText("This form has no published versions yet.")).ToBeVisibleAsync();
        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design version history open with no published versions");
    }

    /// <summary>
    /// Publishes a version, reopens version history against a real row, and scans
    /// <c>RetireConfirmationDialog</c> once <c>VersionHistory</c>'s own "Retire" action opens it —
    /// the panel only ever offers that action on a version currently in the
    /// <c>Published</c> state.
    /// </summary>
    [Fact]
    public async Task VersionHistoryWithAPublishedVersionAndRetireConfirmationDialogHaveNoAccessibilityViolations()
    {
        var formId = await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddLabeledFieldFromPaletteAsync(Page, "Text", "Full legal name");

        await DesignerDriver.PublishAsync(Page, "First published version.");

        // Design.razor's own HandlePublishedAsync navigates to /library on success -- back to
        // /design/{formId} (the same id, still a valid draft session per Design.razor's own
        // FormId-resolution remarks) to reach the toolbar's own Version history button again.
        await Page.GotoAsync($"{BaseUrl}/design/{formId}");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Version history" }).WaitForAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Version history" }).ClickAsync();

        var region = Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Version history" });
        await region.WaitForAsync();
        await Assertions.Expect(region.GetByText("Published", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();

        var retireDialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Retire version 1?" });

        // try/finally, not a plain top-to-bottom sequence: this session's own "Untitled form"
        // card must never survive this test still Published, even if an assertion below fails --
        // every draft opens with that same unrenamed default name (FormDesigner offers no rename
        // affordance yet), so a published-but-never-retired card here collides with
        // DesignAccessibilityTests.AuthorCanFixABlockingIssueAndPublishUsingOnlyTheKeyboard's own
        // "exactly one Published 'Untitled form' card" assertion on the same collection-shared
        // library the instant that test runs next -- turning one regression here into two
        // failures, the second one actively misleading about its own cause.
        try
        {
            await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design version history open with one published version");

            await region.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Retire" }).ClickAsync();
            await retireDialog.WaitForAsync();

            await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design retire-confirmation dialog open");
        }
        finally
        {
            if (await retireDialog.CountAsync() == 0)
            {
                await region.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Retire" }).ClickAsync();
                await retireDialog.WaitForAsync();
            }

            await retireDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Retire" }).ClickAsync();
            await retireDialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        }

        await Assertions.Expect(region.GetByText("Retired", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>
    /// Provokes <c>DesignerCanvas.RequestDelete</c>'s protected path (PRD §4.1): a field a
    /// visibility rule elsewhere still references opens <c>DeleteProtectionDialog</c> instead of
    /// deleting outright, naming the reference.
    /// </summary>
    [Fact]
    public async Task DeleteProtectionDialogHasNoAccessibilityViolations()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);

        await DesignerDriver.AddLabeledFieldFromPaletteAsync(Page, "Text", "Field A");
        await DesignerDriver.AddLabeledFieldFromPaletteAsync(Page, "Email", "Field B");

        // Field B (still selected) gains a visibility rule naming Field A -- AddCondition's own
        // fallback defaults to the first other field, which is exactly Field A here.
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add rule" }).ClickAsync();
        var visibilityDialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Edit visibility rule for 'Field B'" });
        await visibilityDialog.WaitForAsync();
        await visibilityDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add condition" }).ClickAsync();
        await Assertions.Expect(visibilityDialog.GetByLabel("Condition 1 field")).ToBeVisibleAsync();
        await visibilityDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Apply" }).ClickAsync();
        await visibilityDialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });

        // Barrier: PropertiesPanel's own post-close focus-restore to Field B's now-"Edit rule"
        // button is a separate, still-possibly-in-flight render from the dialog's own removal --
        // see WaitForDialogCloseFocusRestoreAsync's own remarks for the race this closes.
        await DesignerDriver.WaitForDialogCloseFocusRestoreAsync(Page, "Edit rule");

        // Delete operates on the roving cursor (_activeNodeId), not the committed selection, so
        // this reaches Field A via the canvas's own keyboard path rather than a click: re-select
        // Field B (still the committed selection -- a same-node reselect arms no focus steal of
        // its own) to get real DOM focus back inside the canvas, then ArrowUp -- Field A was
        // added first, so it sits immediately before Field B -- onto Field A's own row. A click
        // on Field A directly would instead commit it as the new selection and arm
        // PropertiesPanel's own "different node, no other focus intent" steal to its Label input,
        // which would leave Delete's own keydown handler on the canvas without real DOM focus to
        // fire it from.
        await DesignerDriver.SelectCanvasRowAsync(Page, "Field B");
        await DesignerDriver.MoveRovingCursorToRowAsync(Page, "Field A", key: "ArrowUp");
        await Page.Keyboard.PressAsync("Delete");

        var deleteDialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Delete 'Field A'?" });
        await deleteDialog.WaitForAsync();
        await Assertions.Expect(deleteDialog.GetByText("Field B")).ToBeVisibleAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design delete-protection dialog open on a referenced field");
    }

    /// <summary>
    /// The form-wide <c>ValidationRuleEditor</c> only renders while nothing is selected
    /// (<c>PropertiesPanel</c>'s own placeholder branch) — reached here by adding a second page,
    /// whose own fresh selection is page-level, not a node (<c>DesignerEditContext.AddPage</c>'s
    /// own remarks), while the first page's field survives as a rule-target candidate.
    /// </summary>
    [Fact]
    public async Task ValidationRuleEditorWithARuleAndAConditionHasNoAccessibilityViolations()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddLabeledFieldFromPaletteAsync(Page, "Text", "Field A");
        await DesignerDriver.AddLabeledFieldFromPaletteAsync(Page, "Email", "Field B");

        // A fresh page has no node of its own -- selection becomes page-level, so the properties
        // panel's placeholder (and this editor) render instead of Field B's own properties.
        await DesignerDriver.AddPageAsync(Page);

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add rule", Exact = true }).ClickAsync();
        await Assertions.Expect(Page.GetByLabel("Rule 1 target field")).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add condition" }).ClickAsync();
        await Assertions.Expect(Page.GetByLabel("Condition 1 field")).ToBeVisibleAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design validation rule editor open with one rule and one condition");
    }

    [Fact]
    public async Task OptionsEditorWithOptionRowsHasNoAccessibilityViolations()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddFieldFromPaletteAsync(Page, "Dropdown");

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add option" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add option" }).ClickAsync();

        var rows = Page.Locator(".bf-options-editor__row");
        await Assertions.Expect(rows).ToHaveCountAsync(2);

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design options editor open with two option rows");
    }

    /// <summary>
    /// Provokes <c>ExpressionDependencyAnalysis.WouldCreateCycle</c> (Core already detects this;
    /// this scenario is the first to actually drive a UI path into it): Field B's own rule already
    /// names Field A, so reopening Field A's own (empty) rule and letting <c>AddCondition</c>'s own
    /// fallback default to the only other field -- Field B -- and Apply closes the loop.
    /// </summary>
    [Fact]
    public async Task VisibilityRuleCycleRejectionAlertHasNoAccessibilityViolations()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);
        await DesignerDriver.AddLabeledFieldFromPaletteAsync(Page, "Text", "Field A");
        await DesignerDriver.AddLabeledFieldFromPaletteAsync(Page, "Email", "Field B");

        // Field B (still selected) gains a rule naming Field A.
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add rule" }).ClickAsync();
        var bDialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Edit visibility rule for 'Field B'" });
        await bDialog.WaitForAsync();
        await bDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add condition" }).ClickAsync();
        await bDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Apply" }).ClickAsync();
        await bDialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await DesignerDriver.WaitForDialogCloseFocusRestoreAsync(Page, "Edit rule");

        // Field A's own rule, naming Field B -- Field B already names Field A, so this closes a
        // two-node cycle. The properties panel needs to be showing Field A's own properties for
        // "Add rule" to open FIELD A's own dialog next, so this commits Field A as the selection
        // (a click) rather than just moving the roving cursor -- and waits for the product's own
        // deliberate terminal state (focus landing on the Label input) rather than for the canvas
        // row itself, since selecting a different node is exactly what arms that steal.
        await DesignerDriver.SelectCanvasRowAndWaitForLabelFocusAsync(Page, "Field A");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add rule" }).ClickAsync();
        var aDialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Edit visibility rule for 'Field A'" });
        await aDialog.WaitForAsync();
        await aDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add condition" }).ClickAsync();
        await Assertions.Expect(aDialog.GetByLabel("Condition 1 field")).ToBeVisibleAsync();
        await aDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Apply" }).ClickAsync();

        var alert = aDialog.GetByRole(AriaRole.Alert);
        await alert.WaitForAsync();
        await Assertions.Expect(alert).ToContainTextAsync("would create a cycle");

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design visibility rule editor with a rejected cycle alert");
    }

    /// <summary>
    /// The calculation-graph equivalent of
    /// <see cref="VisibilityRuleCycleRejectionAlertHasNoAccessibilityViolations"/>: a Number field
    /// seeds two Calculated-value fields (a calc node is only itself eligible as another calc's
    /// operand once it carries a numeric <c>Format</c>, so the first one needs a plain numeric
    /// anchor), Total B already sums Total A, and reopening Total A to add a second operand
    /// naming Total B closes the loop.
    /// </summary>
    [Fact]
    public async Task CalculationCycleRejectionAlertHasNoAccessibilityViolations()
    {
        await DesignerDriver.GotoNewDesignAsync(Page, BaseUrl);
        await DesignerDriver.AddPageAsync(Page);

        await DesignerDriver.AddLabeledFieldFromPaletteAsync(Page, "Number", "Amount");

        await DesignerDriver.AddLabeledFieldFromPaletteAsync(Page, "Calculated value", "Total A");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add calculation" }).ClickAsync();
        var totalADialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Edit calculation for 'Total A'" });
        await totalADialog.WaitForAsync();
        await totalADialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add operand" }).ClickAsync();
        await totalADialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Apply" }).ClickAsync();
        await totalADialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await DesignerDriver.WaitForDialogCloseFocusRestoreAsync(Page, "Edit calculation");

        await DesignerDriver.AddLabeledFieldFromPaletteAsync(Page, "Calculated value", "Total B");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add calculation" }).ClickAsync();
        var totalBDialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Edit calculation for 'Total B'" });
        await totalBDialog.WaitForAsync();
        await totalBDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add operand" }).ClickAsync();
        await totalBDialog.GetByLabel("Operand 1 field").SelectOptionAsync(new SelectOptionValue { Label = "Total A" });
        await totalBDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Apply" }).ClickAsync();
        await totalBDialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await DesignerDriver.WaitForDialogCloseFocusRestoreAsync(Page, "Edit calculation");

        // Reopen Total A and add a second operand naming Total B -- Total B already names Total
        // A, so this closes a two-node calc cycle. Same reasoning as the visibility-cycle
        // scenario's own reselect: the properties panel needs to be showing Total A's own
        // properties for "Edit calculation" to reopen THAT node's dialog, so this commits the
        // selection and waits for the product's own deliberate Label-focus terminal state.
        await DesignerDriver.SelectCanvasRowAndWaitForLabelFocusAsync(Page, "Total A");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Edit calculation" }).ClickAsync();
        var totalAReopened = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Edit calculation for 'Total A'" });
        await totalAReopened.WaitForAsync();
        await totalAReopened.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add operand" }).ClickAsync();
        await totalAReopened.GetByLabel("Operand 2 field").SelectOptionAsync(new SelectOptionValue { Label = "Total B" });
        await totalAReopened.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Apply" }).ClickAsync();

        var alert = totalAReopened.GetByRole(AriaRole.Alert);
        await alert.WaitForAsync();
        await Assertions.Expect(alert).ToContainTextAsync("would create a cycle");

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/design calculation editor with a rejected cycle alert");
    }
}
