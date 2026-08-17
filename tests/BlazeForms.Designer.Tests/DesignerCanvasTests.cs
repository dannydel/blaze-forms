using BlazeForms.Canvas;
using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Linting;
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
        Assert.Contains("Shown when Employer name is 'x'.", chipTexts);
    }

    [Fact]
    public async Task LogicSummaryChipIsAbsentWhenTheNodeHasNoVisibilityRule()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        Assert.Empty(cut.Find("div.bf-canvas-row").QuerySelectorAll("span.bf-canvas-row__chip--logic"));
        var chipTexts = cut.Find("div.bf-canvas-row").QuerySelectorAll("span.bf-canvas-row__chip")
            .Select(c => c.TextContent)
            .ToArray();
        Assert.DoesNotContain("Required", chipTexts);
        Assert.DoesNotContain("Half width", chipTexts);
    }

    [Fact]
    public async Task LogicSummaryChipDescribesAMultiConditionRuleByCountAndJoin()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        context.UpdateNode(context.Draft.Definition.FindNode("node-b")! with
        {
            VisibleWhen = new ConditionGroup
            {
                Join = ConditionJoin.Any,
                Conditions =
                [
                    new Condition { Field = "node-a", Operator = ConditionOperator.IsNotBlank },
                    new Condition { Field = "node-c", Operator = ConditionOperator.IsNotBlank },
                ],
            },
        });
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        var chipTexts = cut.FindAll("div.bf-canvas-row")[1].QuerySelectorAll("span.bf-canvas-row__chip")
            .Select(c => c.TextContent)
            .ToArray();
        Assert.Contains("Shown when any of 2 conditions.", chipTexts);
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
    public async Task SectionTitleAndDescriptionAreAriaHiddenSoTheListboxOwnsOnlyGroupsAndOptions()
    {
        // A section's title and description are exactly what the enclosing role="group" already
        // names via aria-labelledby -- exposing either as a plain heading/paragraph too would be a
        // second, disallowed kind of child under DesignerCanvas's own role="listbox" (a real
        // regression the Playwright + axe E2E gate caught: "Element has children which are not
        // allowed: h3"). Both must carry aria-hidden="true" so they drop out of the accessibility
        // tree while staying visible on the canvas.
        var definition = new FormDefinition
        {
            Id = "form-1",
            Name = "Form",
            Pages =
            [
                new FormPage
                {
                    Id = "page-1",
                    Sections =
                    [
                        new FormSection
                        {
                            Id = "section-1",
                            Title = "Your details",
                            Description = "We use this to reach you.",
                            Nodes = [new FormNode { Id = "node-a", Type = NodeType.Text, Label = "Field A" }],
                        },
                    ],
                },
            ],
        };

        await using var context = CreateContext(definition);
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        Assert.Equal("true", cut.Find("h3.bf-canvas-section__title").GetAttribute("aria-hidden"));
        Assert.Equal("true", cut.Find("p.bf-canvas-section__description").GetAttribute("aria-hidden"));
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

    // --- Phase 5: the three reorder paths (PRD §4.1) -----------------------------------------

    [Fact]
    public async Task AltArrowDownMovesTheActiveNodeLaterWithinItsOwnSectionAndFocusesIt()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        Assert.Equal(["node-b", "node-a", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id));
        Assert.Equal("node-a", context.Selection.NodeId);
        Assert.Equal(DesignerFocusIntent.Moved, context.Selection.Intent);
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task AltArrowUpAtTheFirstRowIsANoOpAndAnnouncesNothing()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        DesignerAnnouncement? announcement = null;
        context.Announced += a => announcement = a;
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });

        Assert.Equal(["node-a", "node-b", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id));
        Assert.Null(announcement);
        Assert.Equal(DesignerSelection.None, context.Selection);
    }

    [Fact]
    public async Task AltArrowRightMovesTheActiveNodeToTheNextSectionsEnd()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        // node-a is the roving cursor's default (first row) -- moves from section-1 into
        // section-2, appended after node-d.
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight", AltKey = true });

        Assert.Equal(["node-b", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id));
        Assert.Equal(["node-d", "node-a"], context.Draft.Definition.Pages[0].Sections[1].Nodes.Select(n => n.Id));
        Assert.Equal("section-2", context.Selection.SectionId);
        Assert.Equal(DesignerFocusIntent.Moved, context.Selection.Intent);
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task AltArrowLeftOnTheFirstSectionIsANoOpAndAnnouncesNothing()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        DesignerAnnouncement? announcement = null;
        context.Announced += a => announcement = a;
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        // node-a's own section (section-1) is already first on the page -- Alt+← has no earlier
        // section to move into.
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowLeft", AltKey = true });

        Assert.Equal(["node-a", "node-b", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id));
        Assert.Null(announcement);
    }

    [Fact]
    public async Task AltArrowRightOnTheLastSectionIsANoOpAndAnnouncesNothing()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        DesignerAnnouncement? announcement = null;
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        // node-d, in section-2, is the page's last section -- Alt+→ has no later section.
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "End" });
        context.Announced += a => announcement = a;
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight", AltKey = true });

        Assert.Equal(["node-d"], context.Draft.Definition.Pages[0].Sections[1].Nodes.Select(n => n.Id));
        Assert.Null(announcement);
    }

    [Fact]
    public async Task CtrlMOpensTheMoveToPositionDialogForTheActiveNode()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        Assert.Empty(cut.FindAll("div.bf-move-dialog"));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "m", CtrlKey = true });

        var dialog = cut.Find("div.bf-move-dialog");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
    }

    [Fact]
    public async Task DroppingARowOntoAnotherMovesItImmediatelyBeforeTheTarget()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        var rows = cut.FindAll("div.bf-canvas-row");

        // Drag node-a (index 0 in section-1) and drop it onto node-c (index 2) -- lands
        // immediately before node-c, i.e. between node-b and node-c.
        await rows[0].DragStartAsync(new DragEventArgs());
        await rows[2].DropAsync(new DragEventArgs());

        Assert.Equal(["node-b", "node-a", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id));
    }

    [Fact]
    public async Task DroppingARowOntoAnotherSectionsRowMovesItAcrossSections()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        var rows = cut.FindAll("div.bf-canvas-row");

        // node-a (section-1) dropped onto node-d (section-2, its only row) -- lands before it.
        await rows[0].DragStartAsync(new DragEventArgs());
        await rows[3].DropAsync(new DragEventArgs());

        Assert.Equal(["node-b", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id));
        Assert.Equal(["node-a", "node-d"], context.Draft.Definition.Pages[0].Sections[1].Nodes.Select(n => n.Id));
    }

    [Fact]
    public async Task DroppingARowOntoItselfIsANoOpAndAnnouncesNothing()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        DesignerAnnouncement? announcement = null;
        context.Announced += a => announcement = a;
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        var rows = cut.FindAll("div.bf-canvas-row");

        await rows[0].DragStartAsync(new DragEventArgs());
        await rows[0].DropAsync(new DragEventArgs());

        Assert.Equal(["node-a", "node-b", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id));
        Assert.Null(announcement);
    }

    [Fact]
    public async Task DroppingOnAnEmptySectionsContainerAppendsTheDraggedNodeToItsEnd()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionSecondEmptyDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        var row = cut.Find("div.bf-canvas-row"); // node-a, the only row, in section-1
        var sectionRowsWrapper = cut.FindAll("div.bf-canvas-section__rows")[1]; // section-2's, empty

        await row.DragStartAsync(new DragEventArgs());
        await sectionRowsWrapper.DropAsync(new DragEventArgs());

        Assert.Empty(context.Draft.Definition.Pages[0].Sections[0].Nodes);
        Assert.Equal(["node-a"], context.Draft.Definition.Pages[0].Sections[1].Nodes.Select(n => n.Id));
    }

    [Fact]
    public async Task DragEndWithNoDropResetsTheDraggedNodeSoALaterUnrelatedDropIsANoOp()
    {
        // A drag an author cancels (Esc, or releases outside any drop target) still fires
        // dragend, but never reaches DropOnRow or DropOnSection -- without DragEnd resetting
        // _draggedNodeId itself, this drop on an unrelated row (standing in for a stray external
        // drag: a file, selected text) would spuriously move node-a, the row the cancelled drag
        // started on.
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        DesignerAnnouncement? announcement = null;
        context.Announced += a => announcement = a;
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        var rows = cut.FindAll("div.bf-canvas-row");

        await rows[0].DragStartAsync(new DragEventArgs()); // node-a
        await rows[0].DragEndAsync(new DragEventArgs()); // cancelled -- no drop in between
        await rows[2].DropAsync(new DragEventArgs()); // an unrelated later drop, on node-c

        Assert.Equal(["node-a", "node-b", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id));
        Assert.Null(announcement);
    }

    // --- Phase 7: Ctrl+D/Ctrl+Z/Ctrl+Shift+Z, delete protection, and inline lint (PRD §4.1, §8) ---

    [Fact]
    public async Task CtrlDDuplicatesTheActiveNodeAndFocusesTheDuplicate()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "d", CtrlKey = true });

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("div.bf-canvas-row").Count));
        Assert.NotEqual("first-name", context.Selection.NodeId);
        Assert.Equal(DesignerFocusIntent.NewNode, context.Selection.Intent);
        JSInterop.VerifyFocusAsyncInvoke();
    }

    // -- The repeating group drill-in scope (repeating-groups-plan.md, Increment C, PRD §4.1,
    // §11): → enters a repeating row's own scope, Esc and the breadcrumb button both leave it, and
    // every existing affordance (roving focus, Alt+↑/↓ reorder, duplicate, delete, undo/redo)
    // applies unchanged to the group's own children once inside.

    [Fact]
    public async Task ArrowRightOnAnActiveRepeatingRowEntersItsScopeAndFocusesTheFirstChild()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        // group-1 is the roving cursor's default (first row in section-1).
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.Equal("group-1", context.Selection.GroupId);
        Assert.Equal("child-a", context.Selection.NodeId);
        Assert.Equal(DesignerFocusIntent.JumpedTo, context.Selection.Intent);

        var rows = cut.FindAll("div.bf-canvas-row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Full name", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Date of birth", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Outside field", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("Editing fields for 'Household members'", cut.Find("h3.bf-canvas-section__title").TextContent);
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task ArrowRightOnANonRepeatingRowDoesNothing()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.Null(context.Selection.GroupId);
        Assert.Equal(DesignerSelection.None, context.Selection);
    }

    [Fact]
    public async Task BreadcrumbBackButtonExitsScopeAndFocusesTheGroupsOwnRow()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        await cut.Find("button.bf-canvas__back-button").ClickAsync(new MouseEventArgs());

        Assert.Null(context.Selection.GroupId);
        Assert.Equal("group-1", context.Selection.NodeId);
        Assert.Equal(DesignerFocusIntent.JumpedTo, context.Selection.Intent);
        Assert.Equal(2, cut.FindAll("div.bf-canvas-row").Count);
    }

    [Fact]
    public async Task EscapeWhileScopedExitsAndFocusesTheGroupsOwnRow()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Null(context.Selection.GroupId);
        Assert.Equal("group-1", context.Selection.NodeId);
        Assert.Equal(DesignerFocusIntent.JumpedTo, context.Selection.Intent);
    }

    [Fact]
    public async Task EnteringAnEmptyGroupsScopeShowsTheEmptyStateAndFocusesTheScopeHeading()
    {
        await using var context = CreateContext(DesignerTestFixtures.EmptyRepeatingGroupDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.Null(context.Selection.NodeId);
        Assert.Equal("group-1", context.Selection.GroupId);
        Assert.Equal(DesignerFocusIntent.JumpedTo, context.Selection.Intent);
        Assert.Contains("This group has no fields yet", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("div.bf-canvas-row"));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task AltArrowDownWhileScopedReordersWithinTheGroupsOwnChildren()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        var children = context.Draft.Definition.FindNode("group-1")!.Children;
        Assert.Equal(["child-b", "child-a"], children.Select(c => c.Id));
        Assert.Equal("group-1", context.Selection.GroupId);
        Assert.Equal("child-a", context.Selection.NodeId);
    }

    [Fact]
    public async Task CtrlMWhileScopedIsANoOp()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "m", CtrlKey = true });

        Assert.Empty(cut.FindAll("div.bf-move-dialog"));
    }

    [Fact]
    public async Task CtrlDWhileScopedDuplicatesTheActiveChildWithinTheSameGroup()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "d", CtrlKey = true });

        var children = context.Draft.Definition.FindNode("group-1")!.Children;
        Assert.Equal(3, children.Count);
        Assert.Equal("group-1", context.Selection.GroupId);
        Assert.Equal(DesignerFocusIntent.NewNode, context.Selection.Intent);
    }

    [Fact]
    public async Task DeleteWhileScopedOnAnUnreferencedChildDeletesDirectlyAndStaysScoped()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "Delete" });

        Assert.Null(context.Draft.Definition.FindNode("child-a"));
        Assert.Equal("group-1", context.Selection.GroupId);
        Assert.Equal("child-b", context.Selection.NodeId);
        Assert.Empty(cut.FindAll("div.bf-delete-dialog"));
    }

    /// <summary>
    /// The selection/scope snapshot restoring the right view (repeating-groups-plan.md, Increment
    /// C's own explicit requirement): undoing a mutation made while scoped lands back on the same
    /// scoped selection that mutation started from, not merely the same definition.
    /// </summary>
    [Fact]
    public async Task UndoOfAChildMutationRestoresTheScopedSelection()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        context.DeleteNode("child-a");
        Assert.Equal("child-b", context.Selection.NodeId);

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "z", CtrlKey = true });

        Assert.NotNull(context.Draft.Definition.FindNode("child-a"));
        Assert.Equal("group-1", context.Selection.GroupId);
        Assert.Equal("child-a", context.Selection.NodeId);
        Assert.Equal(DesignerFocusIntent.Restored, context.Selection.Intent);
    }

    [Fact]
    public async Task CtrlZUndoesTheMostRecentMutation()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        context.MoveNodeWithinSection("node-a", +1);
        cut.WaitForAssertion(() => Assert.Equal(["node-b", "node-a", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id)));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "z", CtrlKey = true });

        Assert.Equal(["node-a", "node-b", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id));
    }

    [Fact]
    public async Task CtrlShiftZRedoesTheMostRecentlyUndoneMutation()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        context.MoveNodeWithinSection("node-a", +1);
        cut.WaitForAssertion(() => Assert.Equal(["node-b", "node-a", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id)));
        context.Undo();
        cut.WaitForAssertion(() => Assert.Equal(["node-a", "node-b", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id)));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "z", CtrlKey = true, ShiftKey = true });

        Assert.Equal(["node-b", "node-a", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id));
    }

    [Fact]
    public async Task CtrlZWithNothingToUndoIsANoOp()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "z", CtrlKey = true });

        Assert.Equal(["node-a", "node-b", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id));
    }

    [Fact]
    public async Task UndoOfTheVeryFirstMutationRestoresANodelessSelectionAndFocusesTheCanvasRoot()
    {
        // Nothing has ever been selected yet -- DesignerSelection.None -- so the very first
        // mutation's own undo snapshot carries that same node-less, section-less selection.
        // Undoing it restores None with Intent overridden to Restored (PRD §11), which names
        // neither a node nor a section: without this fallback, OnEditContextStateChanged would
        // move no focus at all, stranding it on <body> (WCAG 2.4.3) even though the aria-live
        // region still announces the undo.
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        context.MoveNodeWithinSection("node-a", +1);
        cut.WaitForAssertion(() => Assert.Equal(["node-b", "node-a", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id)));

        context.Undo();

        cut.WaitForAssertion(() => Assert.Equal(["node-a", "node-b", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id)));
        Assert.Null(context.Selection.NodeId);
        Assert.Null(context.Selection.SectionId);
        Assert.Equal(DesignerFocusIntent.Restored, context.Selection.Intent);

        // Once for the move itself landing on node-a, once more for the undo's own node-less
        // fallback, which -- with no section to point to either -- lands on the canvas's own
        // role="listbox" root.
        JSInterop.VerifyFocusAsyncInvoke(2);
    }

    [Fact]
    public async Task UndoOfASecondAddedSectionRestoresASectionOnlySelectionAndFocusesThatSectionsGroup()
    {
        // AddSection's own selection never carries a node either, but its own live commit is
        // tagged NewNode, not Restored -- moving focus there is a later phase's own concern (see
        // this canvas's own class remarks). Undoing a second AddSection, though, restores the
        // first one's still-node-less ForSection selection with Intent overridden to Restored, so
        // the fallback this test exercises is exactly the "still names a section" half of it: the
        // section's own role="group" element, not the canvas root.
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        context.AddSection("page-1");
        cut.WaitForAssertion(() => Assert.Equal(2, context.Draft.Definition.Pages[0].Sections.Count));
        var firstAddedSectionId = context.Draft.Definition.Pages[0].Sections[1].Id;
        context.AddSection("page-1");
        cut.WaitForAssertion(() => Assert.Equal(3, context.Draft.Definition.Pages[0].Sections.Count));

        context.Undo();

        cut.WaitForAssertion(() => Assert.Equal(2, context.Draft.Definition.Pages[0].Sections.Count));
        Assert.Null(context.Selection.NodeId);
        Assert.Equal(firstAddedSectionId, context.Selection.SectionId);
        Assert.Equal(DesignerFocusIntent.Restored, context.Selection.Intent);
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task DeleteOfAnUnreferencedNodeDeletesDirectlyWithNoDialogAndFocusesTheNeighbour()
    {
        await using var context = CreateContext(DesignerTestFixtures.ReferencedFieldDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        // A silent (DesignerFocusIntent.None) selection move to node-unreferenced -- moving the
        // roving cursor there via a keyboard command first (Home/End) would itself call FocusAsync
        // once already, which VerifyFocusAsyncInvoke()'s default single-call assertion below would
        // then double-count.
        context.Select(DesignerSelection.ForNode("node-unreferenced", "page-1", "section-1", DesignerFocusIntent.None));

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "Delete" });

        Assert.Empty(cut.FindAll("div.bf-delete-dialog"));
        Assert.Null(context.Draft.Definition.FindNode("node-unreferenced"));
        Assert.Equal(DesignerFocusIntent.Neighbour, context.Selection.Intent);
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task DeleteOfAReferencedNodeOpensTheProtectionDialogInsteadOfDeleting()
    {
        await using var context = CreateContext(DesignerTestFixtures.ReferencedFieldDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        // The roving cursor defaults to the first row: node-referenced.

        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "Delete" });

        var dialog = cut.Find("div.bf-delete-dialog");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.NotNull(context.Draft.Definition.FindNode("node-referenced"));
    }

    [Fact]
    public async Task ConfirmingDeleteAnywayDeletesLeavesADanglingFr03ReferenceAndFocusesTheNeighbour()
    {
        await using var context = CreateContext(DesignerTestFixtures.ReferencedFieldDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "Delete" });

        await cut.Find("button.bf-delete-dialog__button--danger").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("div.bf-delete-dialog"));
        Assert.Null(context.Draft.Definition.FindNode("node-referenced"));
        // The engine's own Neighbour intent is what carries focus to the row it fell back to --
        // the dialog itself already took an initial FocusAsync call for its own Cancel button, and
        // CloseDeleteDialog re-requests it again once the dialog leaves the DOM, so this test
        // asserts the deterministic focus *intent* rather than an incidental call count.
        Assert.Equal(DesignerFocusIntent.Neighbour, context.Selection.Intent);

        var lint = FormLinter.CreateDefault().Lint(context.Draft.Definition);
        Assert.Contains(lint, r => r.RuleId == LintRuleIds.Fr03);
    }

    [Fact]
    public async Task CancellingTheProtectionDialogTouchesNothingAndRestoresFocusToTheOriginRow()
    {
        await using var context = CreateContext(DesignerTestFixtures.ReferencedFieldDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        await cut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "Delete" });

        await cut.Find("div.bf-delete-dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll("div.bf-delete-dialog"));
        Assert.NotNull(context.Draft.Definition.FindNode("node-referenced"));

        // Once for the dialog's own initial Cancel-button focus, again once CloseDeleteDialog
        // re-requests focus for the (unchanged) origin row after the dialog leaves the DOM.
        JSInterop.VerifyFocusAsyncInvoke(2);
    }

    [Fact]
    public async Task InlineLintMarkerRendersOnlyOnTheCorrectKeyedRowForANodeWithAFinding()
    {
        var definition = new FormDefinition
        {
            Id = "form-1",
            Name = "Two node form",
            Pages =
            [
                new FormPage
                {
                    Id = "page-1",
                    Sections =
                    [
                        new FormSection
                        {
                            Id = "section-1",
                            Nodes =
                            [
                                new FormNode { Id = "node-a", Type = NodeType.Text, Label = "Field A" },
                                new FormNode { Id = "node-b", Type = NodeType.Email },
                            ],
                        },
                    ],
                },
            ],
        };
        await using var context = CreateContext(definition);
        var findings = FormLinter.CreateDefault().Lint(context.Draft.Definition);
        var cut = Render<DesignerCanvas>(p => p
            .Add(f => f.EditContext, context)
            .Add(f => f.ActivePageId, "page-1")
            .Add(f => f.LintResults, findings));

        var rows = cut.FindAll("div.bf-canvas-row");
        Assert.Empty(rows[0].QuerySelectorAll("ul.bf-inline-lint"));
        var markerB = rows[1].QuerySelector("ul.bf-inline-lint");
        Assert.NotNull(markerB);
        Assert.Contains("Blocking", markerB.TextContent, StringComparison.Ordinal);

        // No aria-label on the marker itself: a descendant aria-label would replace the row's
        // own role="option" accessible name with this generic string instead of the finding's
        // real severity+message text, silencing what a screen reader actually hears (code review
        // fix). The severity is still conveyed by TEXT, not color alone -- the assertion above.
        Assert.Null(markerB.GetAttribute("aria-label"));
        Assert.Contains("This field has no label.", rows[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALintPassNamingOnlyOneNodeOnlyReRendersThatNodesOwnRow()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        var rowB = cut.FindComponents<CanvasNodeRow>().Single(r => r.Instance.Node.Id == "node-b");
        var rowBRenderCountBefore = rowB.RenderCount;
        var rowD = cut.FindComponents<CanvasNodeRow>().Single(r => r.Instance.Node.Id == "node-d");
        var rowDRenderCountBefore = rowD.RenderCount;

        IReadOnlyList<LintResult> findings =
        [
            new LintResult { RuleId = LintRuleIds.A11y06, Severity = LintSeverity.Advisory, Message = "Needs a remedy.", NodeId = "node-b" },
        ];
        cut.Render(p => p
            .Add(f => f.EditContext, context)
            .Add(f => f.ActivePageId, "page-1")
            .Add(f => f.LintResults, findings));

        Assert.True(rowB.RenderCount > rowBRenderCountBefore);
        Assert.Equal(rowDRenderCountBefore, rowD.RenderCount);
    }
}
