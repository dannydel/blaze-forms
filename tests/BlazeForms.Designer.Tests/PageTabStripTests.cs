using BlazeForms.Canvas;
using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="PageTabStrip"/>: the plain, labelled page navigation (deliberately not the
/// ARIA tabs pattern -- see <see cref="PageTabStrip"/>'s own remarks), its always-present
/// add-page button, and the add-blank-section / start-from-template affordance an empty page
/// shows (PRD §4.1).
/// </summary>
public sealed class PageTabStripTests : DesignerTestContext
{
    private static DesignerEditContext CreateContext(FormDefinition definition, IFormDefinitionStore? store = null) =>
        new(FormLifecycle.CreateDraft(definition), store ?? new InMemoryFormDefinitionStore());

    [Fact]
    public async Task RendersOnePageButtonPerPageWithTheActiveOneMarkedCurrent()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<PageTabStrip>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        var pageButtons = cut.FindAll("nav.bf-page-tabs__list button");
        Assert.Single(pageButtons);
        Assert.Equal("Details", pageButtons[0].TextContent.Trim());
        Assert.Equal("page", pageButtons[0].GetAttribute("aria-current"));
    }

    [Fact]
    public async Task ANonActivePageButtonCarriesNoAriaCurrent()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        context.AddPage();
        var cut = Render<PageTabStrip>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        var pageButtons = cut.FindAll("nav.bf-page-tabs__list button");
        Assert.Null(pageButtons[1].GetAttribute("aria-current"));
    }

    [Fact]
    public async Task NeitherTheTabsNorTheTabpanelAriaRolesAppearAnywhereInTheMarkup()
    {
        // The strip's own root -- not just the page buttons -- must carry no role="tab",
        // role="tablist", or role="tabpanel": DesignerCanvas's role="listbox" is a real ARIA role
        // of its own, and pairing it with a tab strip's tabpanel would assert a relationship that
        // does not exist (PageTabStrip's own remarks; AGENTS.md invariant #4).
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<PageTabStrip>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        Assert.Empty(cut.FindAll("[role='tab']"));
        Assert.Empty(cut.FindAll("[role='tablist']"));
        Assert.Empty(cut.FindAll("[role='tabpanel']"));
    }

    [Fact]
    public async Task TheAddPageButtonIsASiblingOfTheNavigationRatherThanOneOfItsChildren()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<PageTabStrip>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        var addButton = cut.Find("button.bf-page-tabs__add");
        Assert.Empty(cut.FindAll("nav.bf-page-tabs__list button.bf-page-tabs__add"));
        Assert.Null(addButton.Closest("nav"));
    }

    [Fact]
    public async Task ClickingADifferentPageButtonRaisesActivePageIdChangedWithoutMutatingTheDraft()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        context.AddPage(); // now two pages -- the second one has no title, so it falls back to "Page 2"
        var secondPageId = context.Draft.Definition.Pages[1].Id;
        var pageCountBefore = context.Draft.Definition.Pages.Count;
        string? raised = null;
        var cut = Render<PageTabStrip>(p => p
            .Add(f => f.EditContext, context)
            .Add(f => f.ActivePageId, "page-1")
            .Add(f => f.ActivePageIdChanged, id => raised = id));

        await cut.FindAll("nav.bf-page-tabs__list button")[1].ClickAsync(new MouseEventArgs()); // the second page's button, not the active one

        Assert.Equal(secondPageId, raised);
        Assert.Equal(pageCountBefore, context.Draft.Definition.Pages.Count); // a view-state switch, not a mutation
    }

    [Fact]
    public async Task AddPageButtonAddsAPageThroughTheEditContext()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<PageTabStrip>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        await cut.Find("button.bf-page-tabs__add").ClickAsync(new MouseEventArgs());

        Assert.Equal(2, context.Draft.Definition.Pages.Count);
        Assert.Equal(context.Draft.Definition.Pages[1].Id, context.Selection.PageId);
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("nav.bf-page-tabs__list button").Count));
    }

    [Fact]
    public async Task AnEmptyActivePageShowsTheAddSectionAndStartFromTemplateAffordances()
    {
        await using var context = CreateContext(new FormDefinition
        {
            Id = "form-1",
            Name = "Blank",
            Pages = [new FormPage { Id = "page-1", Title = "Page one" }],
        });
        var cut = Render<PageTabStrip>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        Assert.NotEmpty(cut.Find("div.bf-page-tabs__empty").TextContent);
        var addSectionButton = cut.Find("div.bf-page-tabs__empty-actions button:not([disabled])");
        Assert.Equal("Add blank section", addSectionButton.TextContent);

        var templateButton = cut.Find("div.bf-page-tabs__empty-actions button[disabled]");
        Assert.Equal("Start from template", templateButton.TextContent);
        Assert.Equal("true", templateButton.GetAttribute("aria-disabled"));
    }

    [Fact]
    public async Task AddSectionButtonAddsASectionToTheActivePageAndHidesTheEmptyState()
    {
        await using var context = CreateContext(new FormDefinition
        {
            Id = "form-1",
            Name = "Blank",
            Pages = [new FormPage { Id = "page-1", Title = "Page one" }],
        });
        var cut = Render<PageTabStrip>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        await cut.Find("div.bf-page-tabs__empty-actions button:not([disabled])").ClickAsync(new MouseEventArgs());

        Assert.Single(context.Draft.Definition.Pages[0].Sections);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("div.bf-page-tabs__empty")));
    }

    [Fact]
    public async Task APageWithASectionAlreadyDoesNotShowTheEmptyStateAffordances()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<PageTabStrip>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));

        Assert.Empty(cut.FindAll("div.bf-page-tabs__empty"));
    }
}
