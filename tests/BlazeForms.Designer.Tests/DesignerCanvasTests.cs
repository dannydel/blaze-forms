using BlazeForms.Canvas;
using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="DesignerCanvas"/>: it renders the active page's sections and node rows keyed
/// by identifier, shows exactly the content PRD §4.1 asks each row for, owns a single roving
/// cursor across the whole page (↑/↓/Home/End move it, Enter commits it as
/// <see cref="DesignerEditContext.Selection"/>), follows a mutation's own focus intent onto a new
/// row, and never re-renders an unrelated sibling row.
/// </summary>
/// <remarks>
/// Every test disposes its context via <c>await using</c>, the same reason
/// <c>DesignerEditContextTests</c> does.
/// </remarks>
public sealed class DesignerCanvasTests : DesignerTestContext
{
    private static DesignerEditContext CreateContext(FormDefinition definition, IFormDefinitionStore? store = null) =>
        new(FormLifecycle.CreateDraft(definition), store ?? new InMemoryFormDefinitionStore());

    [Fact]
    public async Task RendersEachSectionAndNodeKeyedWithLabelAndTypeChip()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        Assert.Equal(2, cut.FindAll("div.bf-canvas-section").Count);
        var rows = cut.FindAll("div.bf-canvas-row");
        Assert.Equal(4, rows.Count);

        Assert.Equal("Field A", rows[0].QuerySelector("span.bf-canvas-row__label")!.TextContent);
        var typeChip = rows[0].QuerySelectorAll("span.bf-canvas-row__chip")
            .SingleOrDefault(chip => chip.TextContent == "Text");
        Assert.NotNull(typeChip);
    }

    [Fact]
    public async Task RequiredHalfWidthAndLogicChipsShowOnlyWhenTheNodeCarriesThem()
    {
        await using var context = CreateContext(DesignerTestFixtures.RichNodeDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        var chipTexts = cut.Find("div.bf-canvas-row").QuerySelectorAll("span.bf-canvas-row__chip")
            .Select(c => c.TextContent)
            .ToArray();
        Assert.Contains("Required", chipTexts);
        Assert.Contains("Half width", chipTexts);
        Assert.Contains("Has visibility rule", chipTexts);
    }

    [Fact]
    public async Task LogicSummaryChipIsAbsentWhenTheNodeHasNoVisibilityRule()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        var chipTexts = cut.Find("div.bf-canvas-row").QuerySelectorAll("span.bf-canvas-row__chip")
            .Select(c => c.TextContent)
            .ToArray();
        Assert.DoesNotContain("Has visibility rule", chipTexts);
        Assert.DoesNotContain("Required", chipTexts);
        Assert.DoesNotContain("Half width", chipTexts);
    }

    [Fact]
    public async Task HelpRendersSanitizedMarkdownAndAnUnsafeLinkDoesNotSurvive()
    {
        await using var context = CreateContext(DesignerTestFixtures.RichNodeDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        var help = cut.Find("div.bf-canvas-row__help");
        Assert.Contains("<strong>Bold</strong>", help.InnerHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", help.InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UntitledNodeFallsBackToTheLocalizedTypeName()
    {
        await using var context = CreateContext(DesignerTestFixtures.UntitledNodeDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        Assert.Equal("Untitled Email", cut.Find("span.bf-canvas-row__label").TextContent);
    }

    [Fact]
    public async Task ExactlyOneRowCarriesTabIndexZeroAndItIsTheFirstRowByDefault()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        var rows = cut.FindAll("div.bf-canvas-row");
        Assert.Equal(["0", "-1", "-1", "-1"], rows.Select(r => r.GetAttribute("tabindex")));
        Assert.Equal("Field A", rows[0].QuerySelector("span.bf-canvas-row__label")!.TextContent);
    }

    [Fact]
    public async Task NoRowIsAriaSelectedOnInitialLoadEvenThoughTheFirstRowHoldsTheRovingCursor()
    {
        // DesignerEditContext.Selection starts at DesignerSelection.None (PRD §4.1, §11) -- the
        // roving cursor defaulting to the first row must never be mistaken for a committed
        // selection, which drives aria-selected here (and, later, the properties panel).
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        Assert.Equal(DesignerSelection.None, context.Selection);
        Assert.Empty(cut.FindAll("div.bf-canvas-row[aria-selected='true']"));
        Assert.All(cut.FindAll("div.bf-canvas-row"), row => Assert.Equal("false", row.GetAttribute("aria-selected")));
    }

    [Fact]
    public async Task ArrowDownMovesTheRovingCursorToTheNextRowAndRequestsFocus()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        var rows = cut.FindAll("div.bf-canvas-row");
        Assert.Equal(["-1", "0", "-1", "-1"], rows.Select(r => r.GetAttribute("tabindex")));
        JSInterop.VerifyFocusAsyncInvoke();

        // Moving the roving cursor is not committing a selection -- aria-selected stays false on
        // every row, including the one the cursor just landed on (PRD §11).
        Assert.Empty(cut.FindAll("div.bf-canvas-row[aria-selected='true']"));
    }

    [Fact]
    public async Task ArrowUpMovesTheRovingCursorToThePreviousRow()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });

        var rows = cut.FindAll("div.bf-canvas-row");
        Assert.Equal(["0", "-1", "-1", "-1"], rows.Select(r => r.GetAttribute("tabindex")));
    }

    [Fact]
    public async Task ArrowUpAtTheFirstRowClampsInsteadOfWrapping()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });

        var rows = cut.FindAll("div.bf-canvas-row");
        Assert.Equal(["0", "-1", "-1", "-1"], rows.Select(r => r.GetAttribute("tabindex")));
    }

    [Fact]
    public async Task HomeAndEndJumpToTheFirstAndLastRowAcrossSections()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "End" });
        Assert.Equal(["-1", "-1", "-1", "0"], cut.FindAll("div.bf-canvas-row").Select(r => r.GetAttribute("tabindex")));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "Home" });
        Assert.Equal(["0", "-1", "-1", "-1"], cut.FindAll("div.bf-canvas-row").Select(r => r.GetAttribute("tabindex")));
    }

    [Fact]
    public async Task TabIsNeverHandledAndLeavesTheRovingCursorUntouched()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        // The canvas's own keydown handler only recognizes ArrowUp/ArrowDown/Home/End/Enter --
        // Tab reaching it is a no-op, which is what keeps Tab free to leave the canvas rather
        // than being trapped inside it (PRD §11): this canvas never calls
        // @onkeydown:preventDefault at all (see DesignerCanvas's own remarks on OnKeyDown).
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "Tab" });

        Assert.Equal(["0", "-1", "-1", "-1"], cut.FindAll("div.bf-canvas-row").Select(r => r.GetAttribute("tabindex")));
    }

    [Fact]
    public async Task EnterCommitsTheRovingCursorAsTheEditContextSelection()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("node-a", context.Selection.NodeId);
        Assert.Equal("section-1", context.Selection.SectionId);
        Assert.Equal("page-1", context.Selection.PageId);

        // Committing the selection is what finally flips aria-selected -- exactly the row Enter
        // landed on, and no other.
        var rows = cut.FindAll("div.bf-canvas-row");
        Assert.Equal(["true", "false", "false", "false"], rows.Select(r => r.GetAttribute("aria-selected")));
    }

    [Fact]
    public async Task ClickingARowCommitsItAsTheEditContextSelectionAndMovesTheRovingCursor()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        await cut.FindAll("div.bf-canvas-row")[2].ClickAsync(new MouseEventArgs()); // node-c, in section-1

        Assert.Equal("node-c", context.Selection.NodeId);
        var rows = cut.FindAll("div.bf-canvas-row");
        Assert.Equal(["-1", "-1", "0", "-1"], rows.Select(r => r.GetAttribute("tabindex")));
        Assert.Equal(["false", "false", "true", "false"], rows.Select(r => r.GetAttribute("aria-selected")));
    }

    [Fact]
    public async Task ANewNodeFromAMutationBecomesTheRovingCursorAndTakesFocus()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        context.AddNode(NodeType.Email, "section-1");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("div.bf-canvas-row").Count));

        var rows = cut.FindAll("div.bf-canvas-row");
        Assert.Equal(["-1", "0"], rows.Select(r => r.GetAttribute("tabindex")));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task SelectingOneRowDoesNotReRenderAnUnrelatedSiblingRow()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        // node-d, in section-2 -- unrelated to the node-b click below. bUnit's
        // IRenderedComponent<T>.RenderCount per found child component -- not a raw OuterHtml
        // diff -- is the direct proof CanvasNodeRow.ShouldRender actually skipped this row: a
        // string diff would also flag the harmless, unrelated `@ref` element-reference capture
        // id AngleSharp regenerates on every markup serialization, a false positive that has
        // nothing to do with whether this row's own BuildRenderTree ran again.
        var unrelatedRow = cut.FindComponents<CanvasNodeRow>().Single(row => row.Instance.Node.Id == "node-d");
        var unrelatedRenderCountBefore = unrelatedRow.RenderCount;
        var clickedRow = cut.FindComponents<CanvasNodeRow>().Single(row => row.Instance.Node.Id == "node-b");
        var clickedRenderCountBefore = clickedRow.RenderCount;

        await cut.FindAll("div.bf-canvas-row")[1].ClickAsync(new MouseEventArgs()); // node-b

        Assert.Equal(unrelatedRenderCountBefore, unrelatedRow.RenderCount);
        Assert.True(clickedRow.RenderCount > clickedRenderCountBefore);
    }

    [Fact]
    public async Task NoActivePageRendersTheEmptyStateInsteadOfARowsList()
    {
        await using var context = CreateContext(new FormDefinition { Id = "form-1", Name = "Blank" });
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, (string?)null));

        Assert.Empty(cut.FindAll("div.bf-canvas-row"));
        Assert.NotEmpty(cut.Find("p.bf-canvas__empty").TextContent);
    }

    [Fact]
    public async Task EachSectionsRowsWrapperIsPresentationalSoOptionsAreOwnedDirectlyByTheGroup()
    {
        // div.bf-canvas-section__rows must drop out of the accessibility tree (role="presentation")
        // so each role="option" row resolves to its role="group" section, not to an intervening,
        // unnamed div -- the listbox -> group -> option ownership chain axe's
        // aria-required-parent rule checks for (CanvasSection's own remarks).
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        var rowsWrappers = cut.FindAll("div.bf-canvas-section__rows");
        Assert.Equal(2, rowsWrappers.Count);
        Assert.All(rowsWrappers, wrapper => Assert.Equal("presentation", wrapper.GetAttribute("role")));
    }

    [Fact]
    public async Task TheScrollSuppressionModuleIsImportedOnFirstRender()
    {
        var module = JSInterop.SetupModule(DesignerCanvas.ModulePath);
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));

        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        cut.WaitForAssertion(() => Assert.True(cut.Instance.HasImportedScrollSuppressionModule));
        module.VerifyInvoke("attachScrollSuppression");
    }

    [Fact]
    public async Task DisposeAsyncDisposesTheImportedScrollSuppressionModule()
    {
        JSInterop.SetupModule(DesignerCanvas.ModulePath);
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        cut.WaitForAssertion(() => Assert.True(cut.Instance.HasImportedScrollSuppressionModule));

        await cut.Instance.DisposeAsync();

        Assert.False(cut.Instance.HasImportedScrollSuppressionModule);
    }
}
