using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Serialization;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="VersionHistory"/>: it loads and renders every summary newest first (the
/// store's own <see cref="IFormDefinitionStore.ListVersionsAsync"/> documents oldest first), with
/// text-conveyed state badges; its Retire action opens <see cref="RetireConfirmationDialog"/>,
/// which is the only place a retire actually happens; and its "revise as draft" action produces a
/// brand-new draft from a past version's content without ever mutating that version (AGENTS.md
/// invariant #3).
/// </summary>
public sealed class VersionHistoryTests : DesignerTestContext
{
    private static async Task<InMemoryFormDefinitionStore> SeedTwoVersionsAsync(string formId)
    {
        var store = new InMemoryFormDefinitionStore();
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId))).ConfigureAwait(true);
        await store.PublishAsync(formId, "First cut.", "author-1").ConfigureAwait(true);
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.TwoSectionDefinition(formId))).ConfigureAwait(true);
        await store.PublishAsync(formId, "Added a section.", "author-2").ConfigureAwait(true);
        return store;
    }

    [Fact]
    public async Task RendersAsALabelledRegionWithNoModalAttributes()
    {
        var store = await SeedTwoVersionsAsync("form-1");
        var cut = Render<VersionHistory>(p => p.Add(d => d.FormId, "form-1").Add(d => d.Store, store));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.Instance.Versions.Count));

        var region = cut.Find("div.bf-version-history");
        Assert.Equal("region", region.GetAttribute("role"));
        Assert.Null(region.GetAttribute("aria-modal"));
    }

    [Fact]
    public async Task ListsSummariesNewestFirstEvenThoughTheStoreItselfReturnsOldestFirst()
    {
        var store = await SeedTwoVersionsAsync("form-1");

        // The store's own documented order, confirmed directly, is the opposite of what this
        // panel renders -- proving the reversal actually happens rather than merely matching by
        // coincidence.
        var storeOrder = await store.ListVersionsAsync("form-1");
        Assert.Equal([1, 2], storeOrder.Select(s => s.Version));

        var cut = Render<VersionHistory>(p => p.Add(d => d.FormId, "form-1").Add(d => d.Store, store));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.Instance.Versions.Count));

        Assert.Equal([2, 1], cut.Instance.Versions.Select(s => s.Version));
        var firstRowVersionCell = cut.FindAll("tbody tr")[0].QuerySelector("td");
        Assert.Equal("2", firstRowVersionCell!.TextContent);
    }

    [Fact]
    public async Task StateBadgesCarryTextNotColorAlone()
    {
        var store = await SeedTwoVersionsAsync("form-1");
        var cut = Render<VersionHistory>(p => p.Add(d => d.FormId, "form-1").Add(d => d.Store, store));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.Instance.Versions.Count));

        var badges = cut.FindAll("span.bf-version-history__badge").Select(b => b.TextContent.Trim()).ToArray();
        Assert.All(badges, text => Assert.Equal("Published", text));
    }

    [Fact]
    public async Task EmptyHistoryShowsTheEmptyText()
    {
        var store = new InMemoryFormDefinitionStore();
        var cut = Render<VersionHistory>(p => p.Add(d => d.FormId, "form-1").Add(d => d.Store, store));

        cut.WaitForAssertion(() => Assert.Contains("no published versions", cut.Markup, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RetireOpensTheConfirmationDialogAndConfirmingUpdatesTheListedState()
    {
        var store = await SeedTwoVersionsAsync("form-1");
        var cut = Render<VersionHistory>(p => p.Add(d => d.FormId, "form-1").Add(d => d.Store, store));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.Instance.Versions.Count));

        // Both versions are still Published, so each row carries its own Retire button -- the
        // first one in document order is version 2's, the newest, first-rendered row.
        await cut.FindAll("button").First(b => b.TextContent == "Retire").ClickAsync(new MouseEventArgs());
        Assert.NotEmpty(cut.FindAll("div.bf-retire-dialog"));

        await cut.Find("button.bf-retire-dialog__button--danger").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("div.bf-retire-dialog"));
        cut.WaitForAssertion(() =>
        {
            var retiredRow = cut.Instance.Versions.Single(s => s.Version == 2);
            Assert.Equal(FormLifecycleState.Retired, retiredRow.State);
        });
    }

    [Fact]
    public async Task ReviseAsDraftSavesANewUnpublishedDraftAndRaisesOnRevisedWithoutMutatingTheOriginalVersion()
    {
        var store = await SeedTwoVersionsAsync("form-1");
        var originalVersion1 = await store.GetVersionAsync("form-1", 1);
        // Captured before the revise, not just held as a reference -- InMemoryFormDefinitionStore
        // hands back the SAME instance on every read, so comparing that same reference to itself
        // after the fact would pass even if something mutated it in place. Serializing pins the
        // actual JSON shape, making the immutability assertion below self-enforcing rather than
        // reference-dependent (AGENTS.md invariant #3).
        var originalVersion1Json = FormJson.SerializeDefinition(originalVersion1!.Definition);
        FormVersion? revised = null;
        var closed = false;
        var cut = Render<VersionHistory>(p => p
            .Add(d => d.FormId, "form-1")
            .Add(d => d.Store, store)
            .Add(d => d.OnRevised, (FormVersion v) => revised = v)
            .Add(d => d.OnClosed, () => closed = true));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.Instance.Versions.Count));

        // Row order is newest first (version 2, then version 1) -- the second "Revise as draft"
        // button in document order is version 1's own.
        await cut.FindAll("button").Where(b => b.TextContent == "Revise as draft").ElementAt(1).ClickAsync(new MouseEventArgs());

        Assert.NotNull(revised);
        Assert.Equal(FormLifecycleState.Draft, revised!.State);
        Assert.Equal(0, revised.Version);
        Assert.Equal("form-1", revised.Definition.Id);
        Assert.Equal(DesignerTestFixtures.OneFieldDefinition("form-1").Name, revised.Definition.Name);
        Assert.True(closed);

        var savedDraft = await store.GetDraftAsync("form-1");
        Assert.NotNull(savedDraft);
        Assert.Equal(revised, savedDraft);

        // The version this draft was revised from is completely untouched (AGENTS.md invariant
        // #3) -- same serialized shape, same state, same publish metadata.
        var stillOriginalVersion1 = await store.GetVersionAsync("form-1", 1);
        Assert.Equal(originalVersion1, stillOriginalVersion1);
        Assert.Equal(originalVersion1Json, FormJson.SerializeDefinition(stillOriginalVersion1!.Definition));
        Assert.Equal(FormLifecycleState.Published, stillOriginalVersion1.State);
    }

    [Fact]
    public async Task ReenteringReviseAsDraftAsyncWhileAPriorCallIsStillInFlightSavesTheDraftExactlyOnce()
    {
        // Models a fast double-click under an async host store: the browser delivers a second
        // onclick dispatch before the first call's own _revising-setting statement has had a
        // chance to make its way back to the DOM as a re-render that disables every row's own
        // "Revise as draft" button (VersionHistory.ReviseAsDraftAsync's own remarks). Calling it
        // directly, twice, without awaiting the first in between, reproduces exactly that race
        // deterministically -- the same approach FormRendererSubmissionTests uses for
        // FormRenderer.SubmitAsync's own guard.
        var store = new RecordingFormDefinitionStore();
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition("form-1")));
        await store.PublishAsync("form-1", "First cut.", "author-1");
        var cut = Render<VersionHistory>(p => p.Add(d => d.FormId, "form-1").Add(d => d.Store, store));
        cut.WaitForAssertion(() => Assert.Single(cut.Instance.Versions));
        var saveDraftCallCountBeforeRevising = store.SaveDraftCallCount;

        var firstCall = cut.Instance.ReviseAsDraftAsync(1);
        var secondCall = cut.Instance.ReviseAsDraftAsync(1);
        await Task.WhenAll(firstCall, secondCall);

        Assert.Equal(1, store.SaveDraftCallCount - saveDraftCallCountBeforeRevising);
    }
}
