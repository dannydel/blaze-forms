using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Linting;
using BlazeForms.Palette;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="FormDesigner"/>'s Phase 1 shell: the three labelled docked panes, loading the
/// form's existing working draft or in-memory-creating one through <see cref="IFormDefinitionStore"/>
/// after the first render, the clear failure when no store is registered, that mere opening never
/// persists a draft (PRD §7), and that a keystroke in the palette's search box never re-renders the
/// rest of the shell (AGENTS.md render-discipline standard).
/// </summary>
public sealed class FormDesignerTests : DesignerTestContext
{
    [Fact]
    public async Task RendersThreeLabelledPaneRegionsAndLoadsTheSeededDraft()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));

        // Scoped to the three docked panes specifically -- the linter dock (Phase 7, PRD §8) is
        // its own labelled role="region" too, below the panes, so a bare "every region on the
        // page" query would no longer land on exactly three.
        var regions = cut.FindAll("section.bf-designer__pane[role='region']");
        Assert.Equal(3, regions.Count);
        Assert.Equal("Field palette", regions[0].GetAttribute("aria-label"));
        Assert.Equal("Canvas", regions[1].GetAttribute("aria-label"));
        Assert.Equal("Properties", regions[2].GetAttribute("aria-label"));

        // Proves the seeded draft actually loaded (after the post-render draft load lands, bUnit
        // runs OnAfterRenderAsync as part of Render itself), not just that the shell renders
        // without one.
        cut.WaitForAssertion(() =>
            Assert.Contains("Reference enrollment form", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void ThrowsAClearErrorWhenNoStoreIsRegistered()
    {
        // The missing-registration failure fires synchronously from OnInitialized, before any
        // render or draft load -- this proves the failure survived moving the draft load itself
        // into OnAfterRenderAsync.
        var exception = Assert.ThrowsAny<Exception>(() =>
            Render<FormDesigner>(p => p.Add(f => f.FormId, "form-1")));

        var failure = exception is InvalidOperationException ? exception : exception.InnerException;
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("IFormDefinitionStore", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatesAFreshUntitledDraftInMemoryWithoutPersistingItOnMereOpen()
    {
        var store = new SaveTrackingFormDefinitionStore();
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, "form-new"));

        // The shell renders the in-memory "Untitled form" draft the moment it loads...
        cut.WaitForAssertion(() => Assert.Contains("Untitled form", cut.Markup, StringComparison.Ordinal));

        // ...but opening the designer never wrote it back to the store: PRD §7's "edits
        // accumulate on a new draft" means the draft is persisted on the first edit, not on
        // mere open.
        Assert.Equal(0, store.SaveCount);
        Assert.Null(await store.GetDraftAsync("form-new"));
    }

    [Fact]
    public async Task RevisesTheLatestPublishedVersionAsAnInMemoryDraftWithoutPersistingItOrChangingItsPublishedStatus()
    {
        var store = new SaveTrackingFormDefinitionStore();
        const string formId = "form-published";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        await store.PublishAsync(formId, "Initial publish", "author-1");
        store.ResetSaveCount();
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));

        // The shell renders the in-memory revised draft...
        cut.WaitForAssertion(() =>
            Assert.Contains("Reference enrollment form", cut.Markup, StringComparison.Ordinal));

        // ...but never saved it, so the store still reports no working draft, and the form's
        // library-facing status stays Published rather than flipping to "draft in progress" from
        // a mere open.
        Assert.Equal(0, store.SaveCount);
        Assert.Null(await store.GetDraftAsync(formId));
        var summary = (await store.ListFormsAsync()).Single(s => s.FormId == formId);
        Assert.Equal(FormLifecycleState.Published, summary.State);
    }

    [Fact]
    public async Task LoadsAnExistingDraftAsIsWithoutRevisingOrRecreatingIt()
    {
        var store = new SaveTrackingFormDefinitionStore();
        const string formId = "form-with-draft";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        store.ResetSaveCount();
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));

        cut.WaitForAssertion(() =>
            Assert.Contains("Reference enrollment form", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task DisposingTwiceIsSafe()
    {
        var store = new InMemoryFormDefinitionStore();
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, "form-1"));
        cut.WaitForAssertion(() => Assert.Contains("Untitled form", cut.Markup, StringComparison.Ordinal));

        await cut.Instance.DisposeAsync();
        await cut.Instance.DisposeAsync();
    }

    [Fact]
    public async Task TypingInThePaletteSearchOnlyChangesThePaletteRegion()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));

        // OnAfterRenderAsync's draft load resumes on the render synchronization context and can
        // still be settling when this method returns -- wait for it to land before capturing the
        // "before" snapshots below, or that unrelated render risks getting attributed to the
        // search keystroke.
        cut.WaitForAssertion(() => Assert.Contains("Reference enrollment form", cut.Markup, StringComparison.Ordinal));

        var palette = cut.FindComponent<FieldPalette>();
        var paletteRendersBefore = palette.RenderCount;

        // bUnit's own IRenderedComponent<T>.RenderCount also ticks up for every ancestor of a
        // component that re-renders (it tracks "did this fragment's markup change", not "did
        // this component's own render method run"), so comparing FormDesigner's RenderCount to
        // FieldPalette's would not actually prove anything here. The canvas and properties
        // panes' own markup staying byte-for-byte identical is the direct, meaningful proof that
        // the search keystroke never touches them.
        var canvasMarkupBefore = cut.Find("[aria-label='Canvas']").OuterHtml;
        var propertiesMarkupBefore = cut.Find("[aria-label='Properties']").OuterHtml;

        await cut.Find("input[type='search']").InputAsync(new ChangeEventArgs { Value = "email" });

        Assert.True(palette.RenderCount > paletteRendersBefore);
        Assert.Equal(canvasMarkupBefore, cut.Find("[aria-label='Canvas']").OuterHtml);
        Assert.Equal(propertiesMarkupBefore, cut.Find("[aria-label='Properties']").OuterHtml);
    }

    [Fact]
    public async Task ConstructsExactlyOneEditContextOnceTheDraftLoads()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));
        var editContext = cut.Instance.EditContext!;

        // A mutation driven straight through the context re-renders the shell (via StateChanged)
        // and reaches the hosted AriaLiveRegion (via Announced) -- proving FormDesigner actually
        // wired both, not just constructed the context and left it unobserved.
        editContext.AddNode(NodeType.Text, "section-1");

        cut.WaitForAssertion(() =>
            Assert.Contains("Added", cut.Find("[role='status']").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisposingTheDesignerDisposesItsEditContext()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));
        var editContext = cut.Instance.EditContext!;

        await cut.Instance.DisposeAsync();

        // A mutation attempted after the owning designer disposed its context must not throw --
        // the context's own autosave scheduler is what disposal actually needs to be safe, and
        // this is the only externally observable proof of that from outside the context itself.
        editContext.AddNode(NodeType.Text, "section-1");
    }

    [Fact]
    public async Task PaletteAddTargetsTheActivePagesLastSectionByDefaultAndSelectsTheNewNodeWithNewNodeIntent()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.TwoSectionDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));
        var editContext = cut.Instance.EditContext!;

        // No selection yet -- "section-2" ("Housing") is the active page's last section, the
        // "add near what the author is already looking at" default absent any stronger signal.
        await FindPaletteButton(cut, "Email").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(2, editContext.Draft.Definition.Pages[0].Sections[1].Nodes.Count));
        var added = editContext.Draft.Definition.Pages[0].Sections[1].Nodes[1];
        Assert.Equal(NodeType.Email, added.Type);
        Assert.Equal(added.Id, editContext.Selection.NodeId);
        Assert.Equal("section-2", editContext.Selection.SectionId);
        Assert.Equal(DesignerFocusIntent.NewNode, editContext.Selection.Intent);

        // The new row lands on the canvas, selected and carrying real DOM focus.
        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll("div.bf-canvas-row").Count));
        Assert.Single(cut.FindAll("div.bf-canvas-row[aria-selected='true']"));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task PaletteAddTargetsTheCurrentlySelectedSectionWhenOneIsSelected()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.TwoSectionDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));
        var editContext = cut.Instance.EditContext!;
        editContext.Select(DesignerSelection.ForNode("node-a", "page-1", "section-1", DesignerFocusIntent.None));

        await FindPaletteButton(cut, "Email").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(4, editContext.Draft.Definition.Pages[0].Sections[0].Nodes.Count));
        Assert.Single(editContext.Draft.Definition.Pages[0].Sections[1].Nodes);
    }

    [Fact]
    public async Task PaletteAddCreatesABlankSectionFirstWhenTheActivePageHasNoneYet()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        var definition = new FormDefinition
        {
            Id = formId,
            Name = "Blank",
            Pages = [new FormPage { Id = "page-1", Title = "Page one" }],
        };
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(definition));
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));
        var editContext = cut.Instance.EditContext!;

        await FindPaletteButton(cut, "Email").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Single(editContext.Draft.Definition.Pages[0].Sections));
        Assert.Single(editContext.Draft.Definition.Pages[0].Sections[0].Nodes);
    }

    // --- Phase 7: the linter dock, keyboard-help dialog, and their shell wiring (PRD §4.1, §8) ---

    [Fact]
    public async Task TheLinterDockIsMountedBelowThePanesAsItsOwnLabelledRegion()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("div.bf-linter-dock")));
        var dockRegion = cut.Find("div.bf-linter-dock");
        Assert.Equal("region", dockRegion.GetAttribute("role"));
        Assert.Equal("Linter", dockRegion.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task HelpButtonOpensTheKeyboardHelpDialogAndEscClosesRestoringFocusToTheHelpButton()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);
        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));

        await cut.Find("button.bf-designer__help-button").ClickAsync(new MouseEventArgs());
        Assert.NotEmpty(cut.FindAll("div.bf-keyboard-help"));

        await cut.Find("div.bf-keyboard-help").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll("div.bf-keyboard-help"));

        // Once for the dialog's own initial Close-button focus (KeyboardHelpDialog's own
        // OnAfterRenderAsync), again once CloseKeyboardHelp's _restoreHelpFocusOnNextRender flag
        // is consumed on the render after the dialog has actually left the DOM -- proving Esc
        // never drops focus to <body> (PRD §11), the same "count both the dialog's own initial
        // focus and the trigger's restore" shape DesignerCanvasTests' delete-dialog cancel test
        // uses for itself.
        JSInterop.VerifyFocusAsyncInvoke(2);
    }

    [Fact]
    public async Task HelpButtonOpensTheKeyboardHelpDialogAndItsCloseButtonRestoresFocusToTheHelpButton()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);
        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));

        await cut.Find("button.bf-designer__help-button").ClickAsync(new MouseEventArgs());
        Assert.NotEmpty(cut.FindAll("div.bf-keyboard-help"));

        await cut.Find("button.bf-keyboard-help__button").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("div.bf-keyboard-help"));

        // Same count as the Esc path above -- the dialog's own Close button routes through the
        // exact same CloseKeyboardHelp as Esc does, so both paths restore focus identically.
        JSInterop.VerifyFocusAsyncInvoke(2);
    }

    [Fact]
    public async Task JumpToNodeThroughTheFullShellSwitchesTheActivePageAndFocusesTheRow()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.TwoPageBlockingIssueDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);
        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));

        // The dock's own debounced lint pass (Phase 7) is what hands DesignerCanvas the finding
        // on page-2's unlabelled field -- wait for its jump button to actually show up rather
        // than guessing at the debounce interval.
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.bf-linter-dock__jump")));

        await cut.Find("button.bf-linter-dock__jump").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            var secondPageTab = cut.FindAll("button.bf-page-tabs__tab").Single(b => b.TextContent == "Second page");
            Assert.Equal("page", secondPageTab.GetAttribute("aria-current"));
        });
        Assert.NotEmpty(cut.FindAll("div.bf-canvas-row"));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    // --- Phase 9: the preview toggle and its PreviewPane wiring (PRD §4.1) ---

    [Fact]
    public async Task ThePreviewToggleIsAriaPressedAndEnteringMovesFocusToThePreviewHeading()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);
        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));

        var toggle = cut.Find("button.bf-designer__preview-button");
        Assert.Equal("false", toggle.GetAttribute("aria-pressed"));
        Assert.Empty(cut.FindAll("div.bf-preview-pane"));

        await toggle.ClickAsync(new MouseEventArgs());

        Assert.Equal("true", cut.Find("button.bf-designer__preview-button").GetAttribute("aria-pressed"));
        Assert.NotEmpty(cut.FindAll("div.bf-preview-pane"));
        // DesignerCanvas/PageTabStrip are replaced, not merely covered -- preview takes over the
        // canvas pane's body entirely.
        Assert.Empty(cut.FindAll("div.bf-canvas-row"));

        // PreviewPane's own OnAfterRenderAsync is what moves focus to its heading on entry --
        // FormDesigner itself arms no focus flag for entering, only for leaving.
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task ExitingPreviewViaTheToggleRestoresFocusToTheToggleButton()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);
        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));

        await cut.Find("button.bf-designer__preview-button").ClickAsync(new MouseEventArgs());
        Assert.NotEmpty(cut.FindAll("div.bf-preview-pane"));

        await cut.Find("button.bf-designer__preview-button").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("div.bf-preview-pane"));
        Assert.Equal("false", cut.Find("button.bf-designer__preview-button").GetAttribute("aria-pressed"));
        // Once for PreviewPane's own initial heading focus on entry, again once ExitPreview's
        // _restorePreviewFocusOnNextRender flag is consumed on the render after PreviewPane has
        // actually left the DOM -- the same "count both the entry focus and the trigger's
        // restore" shape the keyboard-help dialog's own Esc test uses for itself.
        JSInterop.VerifyFocusAsyncInvoke(2);
    }

    [Fact]
    public async Task ExitingPreviewViaThePanesOwnExitButtonAlsoRestoresFocusToTheToggleButton()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);
        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));

        await cut.Find("button.bf-designer__preview-button").ClickAsync(new MouseEventArgs());
        Assert.NotEmpty(cut.FindAll("div.bf-preview-pane"));

        await cut.Find("button.bf-preview-pane__exit-button").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("div.bf-preview-pane"));
        Assert.Equal("false", cut.Find("button.bf-designer__preview-button").GetAttribute("aria-pressed"));
        JSInterop.VerifyFocusAsyncInvoke(2);
    }

    [Fact]
    public async Task ExitingAndReenteringPreviewShowsACleanFormBecauseItsTestDataIsDiscarded()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);
        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));

        await cut.Find("button.bf-designer__preview-button").ClickAsync(new MouseEventArgs());
        var field = cut.Find("input[id$='-first-name']");
        await field.InputAsync(new ChangeEventArgs { Value = "throwaway answer" });
        Assert.Equal("throwaway answer", cut.Find("input[id$='-first-name']").GetAttribute("value"));

        await cut.Find("button.bf-designer__preview-button").ClickAsync(new MouseEventArgs()); // exit
        await cut.Find("button.bf-designer__preview-button").ClickAsync(new MouseEventArgs()); // re-enter

        // A brand-new FormRenderer instance inside a brand-new PreviewPane instance -- nothing
        // from the exited session survives (PRD §4.1's "test data is discarded on exit").
        Assert.Equal(string.Empty, cut.Find("input[id$='-first-name']").GetAttribute("value") ?? string.Empty);

        // The working draft itself was never touched by any of this.
        var firstNameNode = cut.Instance.EditContext!.Draft.Definition.FindNode("first-name");
        Assert.False(cut.Instance.EditContext!.IsDirty);
        Assert.NotNull(firstNameNode);
    }

    [Fact]
    public async Task PaletteAddWhileScopedIntoAGroupOnTheSamePageStillTargetsThatGroupsOwnChildren()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.TwoPageRepeatingGroupDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));
        var editContext = cut.Instance.EditContext!;

        // Drill into "group-1" on its own page -- the canvas now shows the scoped view (its back
        // button proves it), and the palette hides Repeating (no nesting) while genuinely scoped.
        editContext.Select(DesignerSelection.ForNode("child-a", "page-1", "section-1", DesignerFocusIntent.None) with { GroupId = "group-1" });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.bf-canvas__back-button")));
        Assert.DoesNotContain(cut.FindAll("span.bf-palette__item-label"), span => span.TextContent == "Repeating group");

        await FindPaletteButton(cut, "Email").ClickAsync(new MouseEventArgs());

        // The add landed in the group's own Children, not the page's top-level section.
        var group = editContext.Draft.Definition.FindNode("group-1")!;
        Assert.Equal(3, group.Children.Count);
        Assert.Equal(NodeType.Email, group.Children[^1].Type);
        Assert.Equal(2, editContext.Draft.Definition.Pages[0].Sections[0].Nodes.Count);
    }

    [Fact]
    public async Task PaletteAddAfterSwitchingPagesWhileScopedTargetsTheNewPageNotTheStaleGroupOnThePageLeft()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.TwoPageRepeatingGroupDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));
        var editContext = cut.Instance.EditContext!;

        // Scope into "group-1" on page-1...
        editContext.Select(DesignerSelection.ForNode("child-a", "page-1", "section-1", DesignerFocusIntent.None) with { GroupId = "group-1" });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.bf-canvas__back-button")));

        // ...then switch to page-2. The selection still carries GroupId "group-1" (a page switch is
        // pure view state, not a mutation), but the scope belongs to the page just left, so the
        // canvas drops it and shows page-2's own top level.
        await cut.FindAll("button.bf-page-tabs__tab").Single(b => b.TextContent == "Coverage page").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("button.bf-canvas__back-button")));

        // Repeating is addable again on page-2's top level -- the stale scope must not keep it hidden.
        Assert.NotNull(FindPaletteButton(cut, "Repeating group"));

        await FindPaletteButton(cut, "Email").ClickAsync(new MouseEventArgs());

        // The field landed in page-2's own section, NOT in the page-1 group's Children.
        Assert.Equal(2, editContext.Draft.Definition.FindNode("group-1")!.Children.Count);
        var page2Section = editContext.Draft.Definition.Pages[1].Sections[0];
        Assert.Equal(2, page2Section.Nodes.Count);
        Assert.Equal(NodeType.Email, page2Section.Nodes[^1].Type);
    }

    private static AngleSharp.Dom.IElement FindPaletteButton(IRenderedComponent<FormDesigner> cut, string label) =>
        cut.FindAll("span.bf-palette__item-label")
            .Single(span => span.TextContent == label)
            .ParentElement!;
}

/// <summary>
/// An <see cref="IFormDefinitionStore"/> decorator wrapping <see cref="InMemoryFormDefinitionStore"/>
/// that counts <see cref="SaveDraftAsync"/> calls, so a test can assert that opening
/// <see cref="FormDesigner"/> on a form never persists the draft it loads or in-memory-creates
/// (PRD §7) — a plain <see cref="InMemoryFormDefinitionStore"/> has no way to observe that on its
/// own, since <see cref="GetDraftAsync"/> returning <see langword="null"/> proves only that nothing
/// was saved by the time the assertion runs, not that a save was never attempted.
/// </summary>
internal sealed class SaveTrackingFormDefinitionStore : IFormDefinitionStore
{
    private readonly InMemoryFormDefinitionStore _inner = new();

    public int SaveCount { get; private set; }

    public void ResetSaveCount() => SaveCount = 0;

    public Task<FormVersion?> GetVersionAsync(string formId, int version, CancellationToken cancellationToken = default) =>
        _inner.GetVersionAsync(formId, version, cancellationToken);

    public Task<FormVersion?> GetLatestPublishedVersionAsync(string formId, CancellationToken cancellationToken = default) =>
        _inner.GetLatestPublishedVersionAsync(formId, cancellationToken);

    public Task<FormVersion?> GetDraftAsync(string formId, CancellationToken cancellationToken = default) =>
        _inner.GetDraftAsync(formId, cancellationToken);

    public Task SaveDraftAsync(FormVersion draft, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return _inner.SaveDraftAsync(draft, cancellationToken);
    }

    public Task DeleteDraftAsync(string formId, CancellationToken cancellationToken = default) =>
        _inner.DeleteDraftAsync(formId, cancellationToken);

    public Task<FormVersion> PublishAsync(
        string formId,
        string changeNote,
        string author,
        CancellationToken cancellationToken = default) =>
        _inner.PublishAsync(formId, changeNote, author, cancellationToken);

    public Task RetireAsync(string formId, int version, CancellationToken cancellationToken = default) =>
        _inner.RetireAsync(formId, version, cancellationToken);

    public Task<IReadOnlyList<FormVersionSummary>> ListVersionsAsync(
        string formId,
        CancellationToken cancellationToken = default) =>
        _inner.ListVersionsAsync(formId, cancellationToken);

    public Task<IReadOnlyList<FormVersionSummary>> ListFormsAsync(CancellationToken cancellationToken = default) =>
        _inner.ListFormsAsync(cancellationToken);
}
