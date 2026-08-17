using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Rules;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="CalculationEditor"/>: it seeds its working state from the node's current
/// calculation (or the absence of one), the operation/format selects and operand rows round-trip
/// into a well-formed <see cref="CalcExpression"/>, a candidate that would close a calculation
/// cycle is REJECTED without ever touching the draft and renders the named path in a
/// <c>role="alert"</c> with focus moved there, and Esc/Cancel close without mutating
/// (PRD §4.1, §5, §13, §11).
/// </summary>
public sealed class CalculationEditorTests : DesignerTestContext
{
    private static DesignerEditContext CreateContext(FormDefinition definition, IFormDefinitionStore? store = null) =>
        new(FormLifecycle.CreateDraft(definition), store ?? new InMemoryFormDefinitionStore());

    [Fact]
    public async Task RendersAsAFocusLabelledModalDialog()
    {
        await using var context = CreateContext(DesignerTestFixtures.CalcNodeDefinition("form-1"));
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-calc"));

        var dialog = cut.Find("div.bf-calc-dialog");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        var labelledBy = dialog.GetAttribute("aria-labelledby");
        Assert.NotNull(labelledBy);
        Assert.Equal(cut.Find("h2").Id, labelledBy);
    }

    [Fact]
    public async Task SeedsFromANodeWithNoExistingCalculation()
    {
        await using var context = CreateContext(DesignerTestFixtures.CalcNodeDefinition("form-1"));
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-calc"));

        Assert.Empty(cut.FindAll("div.bf-calc-operand-row"));
        var topSelects = cut.FindAll(".bf-calc-dialog > div.bf-calc-dialog__field select");
        var operationSelect = (AngleSharp.Html.Dom.IHtmlSelectElement)topSelects[0];
        var formatSelect = (AngleSharp.Html.Dom.IHtmlSelectElement)topSelects[1];
        Assert.Equal(nameof(CalcOperation.Sum), operationSelect.Value);
        Assert.Equal(nameof(CalcFormat.Number), formatSelect.Value);
    }

    [Fact]
    public async Task SeedsFromANodesExistingCalculation()
    {
        await using var context = CreateContext(DesignerTestFixtures.CalcNodeWithExpressionDefinition("form-1"));
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-calc"));

        Assert.Single(cut.FindAll("div.bf-calc-operand-row"));
        var topSelects = cut.FindAll(".bf-calc-dialog > div.bf-calc-dialog__field select");
        var operationSelect = (AngleSharp.Html.Dom.IHtmlSelectElement)topSelects[0];
        Assert.Equal(nameof(CalcOperation.Sum), operationSelect.Value);
    }

    [Fact]
    public async Task AddingOperandsChangingOperationAndFormatAndApplyingBuildsAWellFormedExpression()
    {
        await using var context = CreateContext(DesignerTestFixtures.CalcNodeDefinition("form-1"));
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-calc"));

        // Re-finds between each change: changing one select re-renders the dialog, which would
        // otherwise leave an already-found reference to the other stale (bUnit's own
        // "UnknownEventHandlerIdException" gotcha).
        await cut.FindAll(".bf-calc-dialog > div.bf-calc-dialog__field select")[0].ChangeAsync(nameof(CalcOperation.Multiply));
        await cut.FindAll(".bf-calc-dialog > div.bf-calc-dialog__field select")[1].ChangeAsync(nameof(CalcFormat.Currency));

        await cut.Find("button.bf-calc-dialog__add-button").ClickAsync(new MouseEventArgs());
        await cut.Find("button.bf-calc-dialog__add-button").ClickAsync(new MouseEventArgs());

        var rows = cut.FindAll("div.bf-calc-operand-row");
        Assert.Equal(2, rows.Count);

        // Both rows default to "node-a" (the first candidate field) -- point the second at
        // "node-b" via its own Field select (the second select in a Field-kind row).
        await rows[1].QuerySelectorAll("select")[1].ChangeAsync("node-b");

        await cut.Find("button.bf-calc-dialog__button--primary").ClickAsync(new MouseEventArgs());

        var calculation = context.Draft.Definition.FindNode("node-calc")!.Calculation;
        Assert.NotNull(calculation);
        Assert.Equal(CalcOperation.Multiply, calculation.Operation);
        Assert.Equal(CalcFormat.Currency, calculation.Format);
        Assert.Equal(2, calculation.Operands.Count);
        Assert.Equal("node-a", calculation.Operands[0].Field);
        Assert.Equal("node-b", calculation.Operands[1].Field);
    }

    /// <summary>
    /// The authoring-time counterpart to the linter's blocking FR-04 rule (repeating-groups-plan.md,
    /// Increment C): a calc node inside a repeating group offers its own numeric siblings and
    /// every top-level numeric field as operand candidates, but never a different group's own
    /// child -- narrowing <c>CandidateFieldsFor</c> already applies to the operation's own
    /// operand typing (numeric vs. date), unaffected by this boundary filter running alongside it.
    /// </summary>
    [Fact]
    public async Task OperandFieldPickerForACalcInsideAGroupOffersSiblingsAndTopLevelFieldsButNotAnotherGroupsChildren()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoRepeatingGroupsWithCalcDefinition("form-1"));
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "calc-in-group"));

        await cut.Find("button.bf-calc-dialog__add-button").ClickAsync(new MouseEventArgs());

        var options = cut.Find("div.bf-calc-operand-row").QuerySelectorAll("select")[1]
            .QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToArray();

        Assert.Contains("child-a2", options);
        Assert.Contains("node-outside", options);
        Assert.DoesNotContain("child-c", options);
    }

    /// <summary>
    /// Switching from a numeric operation to a date one must never leave a numeric field
    /// stranded on the date operation, silently Apply-able as a calculation that can only ever
    /// evaluate to no value (code review fix #2). The stale selection is cleared, not defensively
    /// unioned back in, so the author sees the mismatch and has to re-pick.
    /// </summary>
    [Fact]
    public async Task SwitchingFromANumericToADateOperationClearsAFieldOperandThatNoLongerMatches()
    {
        await using var context = CreateContext(DesignerTestFixtures.CalcNodeWithExpressionDefinition("form-1"));
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-calc"));

        // Seeded with a Sum over "node-a" (a Number field, calc-engine-plan.md's own numeric
        // candidate typing).
        var rowBefore = cut.Find("div.bf-calc-operand-row");
        var fieldSelectBefore = (AngleSharp.Html.Dom.IHtmlSelectElement)rowBefore.QuerySelectorAll("select")[1];
        Assert.Equal("node-a", fieldSelectBefore.Value);

        await cut.FindAll(".bf-calc-dialog > div.bf-calc-dialog__field select")[0].ChangeAsync(nameof(CalcOperation.DateAddDays));

        var rowAfter = cut.Find("div.bf-calc-operand-row");
        var fieldSelectAfter = (AngleSharp.Html.Dom.IHtmlSelectElement)rowAfter.QuerySelectorAll("select")[1];

        // The stale numeric selection is gone -- the placeholder option is selected, not "node-a".
        Assert.Equal(string.Empty, fieldSelectAfter.Value);

        // Applying without re-picking commits a calculation whose sole operand is fully blank --
        // never one that silently still names "node-a" under an operation it was never validated
        // against.
        await cut.Find("button.bf-calc-dialog__button--primary").ClickAsync(new MouseEventArgs());
        var calculation = context.Draft.Definition.FindNode("node-calc")!.Calculation;
        Assert.NotNull(calculation);
        Assert.Equal(CalcOperation.DateAddDays, calculation.Operation);
        Assert.Null(calculation.Operands[0].Field);
    }

    /// <summary>
    /// Switching between two operations that SHARE a category (both numeric, here) must touch
    /// nothing -- every existing field selection is still exactly as valid as it was.
    /// </summary>
    [Fact]
    public async Task SwitchingBetweenTwoNumericOperationsNeverClearsExistingFieldOperands()
    {
        await using var context = CreateContext(DesignerTestFixtures.CalcNodeWithExpressionDefinition("form-1"));
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-calc"));

        await cut.FindAll(".bf-calc-dialog > div.bf-calc-dialog__field select")[0].ChangeAsync(nameof(CalcOperation.Multiply));

        var row = cut.Find("div.bf-calc-operand-row");
        var fieldSelect = (AngleSharp.Html.Dom.IHtmlSelectElement)row.QuerySelectorAll("select")[1];
        Assert.Equal("node-a", fieldSelect.Value);
    }

    [Fact]
    public async Task ClearingToZeroOperandsAndApplyingClearsTheCalculation()
    {
        await using var context = CreateContext(DesignerTestFixtures.CalcNodeWithExpressionDefinition("form-1"));
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-calc"));

        await cut.Find("div.bf-calc-operand-row button.bf-calc-operand-row__remove-button").ClickAsync(new MouseEventArgs());
        await cut.Find("button.bf-calc-dialog__button--primary").ClickAsync(new MouseEventArgs());

        Assert.Null(context.Draft.Definition.FindNode("node-calc")!.Calculation);
    }

    [Fact]
    public async Task ApplyingRaisesOnClosed()
    {
        await using var context = CreateContext(DesignerTestFixtures.CalcNodeDefinition("form-1"));
        var closed = false;
        var cut = Render<CalculationEditor>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.NodeId, "node-calc")
            .Add(d => d.OnClosed, () => closed = true));

        await cut.Find("button.bf-calc-dialog__button--primary").ClickAsync(new MouseEventArgs());

        Assert.True(closed);
    }

    [Fact]
    public async Task EscCancelsWithoutTouchingTheDraftAndRaisesOnClosed()
    {
        await using var context = CreateContext(DesignerTestFixtures.CalcNodeWithExpressionDefinition("form-1"));
        var closed = false;
        var cut = Render<CalculationEditor>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.NodeId, "node-calc")
            .Add(d => d.OnClosed, () => closed = true));

        var before = context.Draft.Definition;
        await cut.Find("div.bf-calc-dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Same(before, context.Draft.Definition);
        Assert.True(closed);
    }

    [Fact]
    public async Task CancelButtonCancelsWithoutTouchingTheDraft()
    {
        await using var context = CreateContext(DesignerTestFixtures.CalcNodeWithExpressionDefinition("form-1"));
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-calc"));

        var before = context.Draft.Definition;
        await cut.Find("button.bf-calc-dialog__button:not(.bf-calc-dialog__button--primary)").ClickAsync(new MouseEventArgs());

        Assert.Same(before, context.Draft.Definition);
    }

    /// <summary>
    /// A candidate that would create a direct two-node calculation cycle is REJECTED: the node is
    /// NOT mutated, the dialog stays open, the named path renders in a <c>role="alert"</c>, and
    /// focus moves there.
    /// </summary>
    [Fact]
    public async Task ACycleCreatingCandidateIsRejectedWithTheNamedPathAndFocusMovesToTheAlert()
    {
        // calc-b already depends on calc-a; giving calc-a a calculation that reads calc-b -- the
        // only numeric candidate CandidateFieldsFor offers it -- would close calc-a -> calc-b ->
        // calc-a.
        await using var context = CreateContext(DesignerTestFixtures.TwoCalcNodesDefinition("form-1"));
        var before = context.Draft.Definition;
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "calc-a"));

        await cut.Find("button.bf-calc-dialog__add-button").ClickAsync(new MouseEventArgs());
        await cut.Find("button.bf-calc-dialog__button--primary").ClickAsync(new MouseEventArgs());

        var alert = cut.Find("div[role='alert']");
        Assert.Contains("Calc A", alert.TextContent, StringComparison.Ordinal);
        Assert.Contains("Calc B", alert.TextContent, StringComparison.Ordinal);
        Assert.Same(before, context.Draft.Definition);

        // Once for the dialog's own first-render focus (the Operation select), once more for the
        // freshly added row's own Kind select, and once more for the rejection moving focus to
        // the alert.
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 3);
    }

    // --- Focus management on add/remove (PRD §11, WCAG 2.4.3) --------------------------------

    [Fact]
    public async Task AddingAnOperandFocusesTheNewRowsKindSelect()
    {
        await using var context = CreateContext(DesignerTestFixtures.CalcNodeDefinition("form-1"));
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-calc"));

        await cut.Find("button.bf-calc-dialog__add-button").ClickAsync(new MouseEventArgs());

        Assert.Single(cut.FindAll("div.bf-calc-operand-row"));

        // Once for the dialog's own first-render focus (the Operation select), once more for the
        // freshly added row's own Kind select.
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 2);
    }

    /// <summary>
    /// Builds a definition where "node-calc" already carries a three-operand calculation -- two
    /// field references and a literal -- so removing any one of them can never close a cycle.
    /// </summary>
    private static FormDefinition ThreeOperandCalcDefinition()
    {
        var definition = DesignerTestFixtures.CalcNodeDefinition("form-1");
        return definition with
        {
            Pages =
            [
                definition.Pages[0] with
                {
                    Sections =
                    [
                        definition.Pages[0].Sections[0] with
                        {
                            Nodes =
                            [
                                definition.Pages[0].Sections[0].Nodes[0],
                                definition.Pages[0].Sections[0].Nodes[1],
                                definition.Pages[0].Sections[0].Nodes[2] with
                                {
                                    Calculation = new CalcExpression
                                    {
                                        Operation = CalcOperation.Sum,
                                        Operands =
                                        [
                                            new CalcOperand { Field = "node-a" },
                                            new CalcOperand { Field = "node-b" },
                                            new CalcOperand { Number = 5m },
                                        ],
                                        Format = CalcFormat.Number,
                                    },
                                },
                            ],
                        },
                    ],
                },
            ],
        };
    }

    [Fact]
    public async Task RemovingAMiddleOperandFocusesThePreviousRowsKindSelect()
    {
        await using var context = CreateContext(ThreeOperandCalcDefinition());
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-calc"));

        var rows = cut.FindAll("div.bf-calc-operand-row");
        Assert.Equal(3, rows.Count);

        await rows[1].QuerySelector("button.bf-calc-operand-row__remove-button")!.ClickAsync(new MouseEventArgs());

        Assert.Equal(2, cut.FindAll("div.bf-calc-operand-row").Count);

        // Once for the dialog's own first-render focus, once more for the previous row's own
        // Kind select.
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 2);
    }

    [Fact]
    public async Task RemovingTheFirstOperandFocusesTheNewFirstRowsKindSelect()
    {
        await using var context = CreateContext(ThreeOperandCalcDefinition());
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-calc"));

        var rows = cut.FindAll("div.bf-calc-operand-row");
        Assert.Equal(3, rows.Count);

        await rows[0].QuerySelector("button.bf-calc-operand-row__remove-button")!.ClickAsync(new MouseEventArgs());

        Assert.Equal(2, cut.FindAll("div.bf-calc-operand-row").Count);

        // Once for the dialog's own first-render focus, once more for the row that slides into
        // the first position.
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 2);
    }

    [Fact]
    public async Task RemovingTheLastRemainingOperandFocusesTheAddOperandButton()
    {
        await using var context = CreateContext(DesignerTestFixtures.CalcNodeWithExpressionDefinition("form-1"));
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-calc"));

        await cut.Find("div.bf-calc-operand-row button.bf-calc-operand-row__remove-button").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("div.bf-calc-operand-row"));

        // Once for the dialog's own first-render focus, once more for the add-operand button.
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 2);
    }

    [Fact]
    public async Task TheFocusTrapModuleIsImportedAndDisposed()
    {
        var module = JSInterop.SetupModule(CalculationEditor.ModulePath);
        await using var context = CreateContext(DesignerTestFixtures.CalcNodeDefinition("form-1"));
        var cut = Render<CalculationEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-calc"));

        cut.WaitForAssertion(() => Assert.True(cut.Instance.HasImportedModule));
        module.VerifyInvoke("attachFocusTrap");

        await cut.Instance.DisposeAsync();

        Assert.False(cut.Instance.HasImportedModule);
    }
}
