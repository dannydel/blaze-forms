using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Palette;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components;
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

        var regions = cut.FindAll("[role='region']");
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
