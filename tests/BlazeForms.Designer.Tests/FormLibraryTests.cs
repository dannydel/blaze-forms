using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="FormLibrary"/>: it loads every form from a seeded
/// <see cref="IFormDefinitionStore.ListFormsAsync"/>; search narrows by name and author; the status
/// filter narrows by <see cref="FormLifecycleState"/>; sort reorders by name, submission count, or
/// last-published date; the cards⇄table toggle preserves whatever the filters currently show and is
/// itself <c>aria-pressed</c>; status badges carry text; and the open control on either view raises
/// <see cref="FormLibrary.OnOpenInDesigner"/> with the right form ID. Also proves the two facets
/// this phase deliberately omits (PRD §4.4's "program" facet and its "blocking-issues-only" filter —
/// see <see cref="FormLibrary"/>'s own remarks) ship no dead control.
/// </summary>
public sealed class FormLibraryTests : DesignerTestContext
{
    /// <summary>
    /// Seeds four forms spanning every <see cref="FormLifecycleState"/>, with distinct names,
    /// authors, publish times, and submission counts so search, every sort mode, and the status
    /// filter each have an unambiguous expected order to assert against:
    /// <list type="bullet">
    /// <item><description>"Alpha form" — a draft, no author yet, 0 submissions.</description></item>
    /// <item><description>"Mu form" — published then retired by "alice", the earliest publish
    /// time, 5 submissions.</description></item>
    /// <item><description>"Zeta form" — published by "alice", the middle publish time, 1
    /// submission.</description></item>
    /// <item><description>"Nu form" — published by "bob", the latest publish time, 3
    /// submissions.</description></item>
    /// </list>
    /// <see cref="InMemoryFormDefinitionStore"/> itself always summarizes submission count as zero
    /// (it does not track submissions at all — <see cref="FormVersionSummary.SubmissionCount"/>'s
    /// own remarks), so this wraps one in <see cref="SubmissionCountOverrideStore"/> purely to give
    /// the sort-by-submission-count and submission-count-display tests real numbers to assert
    /// against; every other operation goes straight through to the real store underneath.
    /// </summary>
    private static async Task<IFormDefinitionStore> SeedFourFormsAsync()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var inner = new InMemoryFormDefinitionStore(clock);

        await inner.SaveDraftAsync(FormLifecycle.CreateDraft(Definition("form-mu", "Mu form"))).ConfigureAwait(true);
        await inner.PublishAsync("form-mu", "Initial cut.", "alice").ConfigureAwait(true);
        await inner.RetireAsync("form-mu", 1).ConfigureAwait(true);

        clock.Advance(TimeSpan.FromDays(1));
        await inner.SaveDraftAsync(FormLifecycle.CreateDraft(Definition("form-zeta", "Zeta form"))).ConfigureAwait(true);
        await inner.PublishAsync("form-zeta", "Initial cut.", "alice").ConfigureAwait(true);

        clock.Advance(TimeSpan.FromDays(1));
        await inner.SaveDraftAsync(FormLifecycle.CreateDraft(Definition("form-nu", "Nu form"))).ConfigureAwait(true);
        await inner.PublishAsync("form-nu", "Initial cut.", "bob").ConfigureAwait(true);

        await inner.SaveDraftAsync(FormLifecycle.CreateDraft(Definition("form-alpha", "Alpha form"))).ConfigureAwait(true);

        return new SubmissionCountOverrideStore(
            inner,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["form-mu"] = 5,
                ["form-zeta"] = 1,
                ["form-nu"] = 3,
            });
    }

    private static FormDefinition Definition(string id, string name) => new() { Id = id, Name = name };

    [Fact]
    public async Task RendersALabelledRegionAndLoadsEverySeededForm()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);

        var cut = Render<FormLibrary>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        var region = cut.Find("div.bf-library");
        Assert.Equal("region", region.GetAttribute("role"));
        Assert.Equal("Form library", region.GetAttribute("aria-label"));
    }

    [Fact]
    public void ThrowsAClearErrorWhenNoStoreIsRegistered()
    {
        var exception = Assert.ThrowsAny<Exception>(() => Render<FormLibrary>());

        var failure = exception is InvalidOperationException ? exception : exception.InnerException;
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("IFormDefinitionStore", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyStoreShowsTheEmptyText()
    {
        Services.AddSingleton<IFormDefinitionStore>(new InMemoryFormDefinitionStore());

        var cut = Render<FormLibrary>();

        cut.WaitForAssertion(() => Assert.Contains("No forms yet.", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchNarrowsByNameAndByAuthor()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        var cut = Render<FormLibrary>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        await cut.Find("input[type='search']").InputAsync(new ChangeEventArgs { Value = "alpha" });
        Assert.Equal(["Alpha form"], cut.Instance.FilteredForms.Select(f => f.Name));

        await cut.Find("input[type='search']").InputAsync(new ChangeEventArgs { Value = "bob" });
        Assert.Equal(["Nu form"], cut.Instance.FilteredForms.Select(f => f.Name));

        await cut.Find("input[type='search']").InputAsync(new ChangeEventArgs { Value = "alice" });
        Assert.Equal(["Mu form", "Zeta form"], cut.Instance.FilteredForms.Select(f => f.Name).OrderBy(n => n));
    }

    [Fact]
    public async Task SearchThatMatchesNothingShowsTheNoResultsText()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        var cut = Render<FormLibrary>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        await cut.Find("input[type='search']").InputAsync(new ChangeEventArgs { Value = "no-such-form" });

        Assert.Contains("No forms match your search and filters.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusFilterNarrowsByLifecycleState()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        var cut = Render<FormLibrary>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        await cut.Find("select.bf-library__select").ChangeAsync("Published");
        Assert.Equal(["Nu form", "Zeta form"], cut.Instance.FilteredForms.Select(f => f.Name).OrderBy(n => n));

        await cut.Find("select.bf-library__select").ChangeAsync("Draft");
        Assert.Equal(["Alpha form"], cut.Instance.FilteredForms.Select(f => f.Name));

        await cut.Find("select.bf-library__select").ChangeAsync("Retired");
        Assert.Equal(["Mu form"], cut.Instance.FilteredForms.Select(f => f.Name));

        await cut.Find("select.bf-library__select").ChangeAsync("");
        Assert.Equal(4, cut.Instance.FilteredForms.Count);
    }

    [Fact]
    public async Task DefaultSortOrdersByNameAscending()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        var cut = Render<FormLibrary>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        Assert.Equal(
            ["Alpha form", "Mu form", "Nu form", "Zeta form"],
            cut.Instance.FilteredForms.Select(f => f.Name));
    }

    [Fact]
    public async Task SortBySubmissionCountOrdersDescending()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        var cut = Render<FormLibrary>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        await cut.FindAll("select.bf-library__select")[1].ChangeAsync("SubmissionCount");

        Assert.Equal(
            ["Mu form", "Nu form", "Zeta form", "Alpha form"],
            cut.Instance.FilteredForms.Select(f => f.Name));
    }

    [Fact]
    public async Task SortByLastPublishedOrdersNewestFirstWithDraftsLast()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        var cut = Render<FormLibrary>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        await cut.FindAll("select.bf-library__select")[1].ChangeAsync("LastPublished");

        Assert.Equal(
            ["Nu form", "Zeta form", "Mu form", "Alpha form"],
            cut.Instance.FilteredForms.Select(f => f.Name));
    }

    [Fact]
    public async Task StatusBadgesCarryTextNotColorAlone()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        var cut = Render<FormLibrary>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        var badgeTexts = cut.FindAll("span.bf-form-card__badge").Select(b => b.TextContent.Trim()).OrderBy(t => t);

        Assert.Equal(["Draft", "Published", "Published", "Retired"], badgeTexts);
    }

    [Fact]
    public async Task ViewToggleDefaultsToCardsAndIsAriaPressed()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        var cut = Render<FormLibrary>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        var buttons = cut.FindAll("button.bf-library__view-button");
        Assert.Equal("true", buttons[0].GetAttribute("aria-pressed"));
        Assert.Equal("false", buttons[1].GetAttribute("aria-pressed"));
        Assert.NotEmpty(cut.FindAll("ul.bf-library__cards"));
        Assert.Empty(cut.FindAll("table.bf-form-table"));
    }

    [Fact]
    public async Task TogglingToTablePreservesTheCurrentlyFilteredSetAndFlipsAriaPressed()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        var cut = Render<FormLibrary>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        // Filter down to two forms before switching views -- proves the toggle carries the
        // filtered set across, not just the unfiltered whole library.
        await cut.Find("select.bf-library__select").ChangeAsync("Published");
        Assert.Equal(2, cut.Instance.FilteredForms.Count);

        var buttons = cut.FindAll("button.bf-library__view-button");
        await buttons[1].ClickAsync(new MouseEventArgs());

        var refreshedButtons = cut.FindAll("button.bf-library__view-button");
        Assert.Equal("false", refreshedButtons[0].GetAttribute("aria-pressed"));
        Assert.Equal("true", refreshedButtons[1].GetAttribute("aria-pressed"));

        Assert.Empty(cut.FindAll("ul.bf-library__cards"));
        var rows = cut.FindAll("table.bf-form-table tbody tr");
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task TableHeadersCarryScopeCol()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        var cut = Render<FormLibrary>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        await cut.FindAll("button.bf-library__view-button")[1].ClickAsync(new MouseEventArgs());

        var headers = cut.FindAll("table.bf-form-table th");
        Assert.Equal(6, headers.Count);
        Assert.All(headers, header => Assert.Equal("col", header.GetAttribute("scope")));
    }

    [Fact]
    public async Task OpenControlOnACardRaisesOnOpenInDesignerWithTheFormId()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        string? opened = null;
        var cut = Render<FormLibrary>(p => p.Add(l => l.OnOpenInDesigner, (string id) => opened = id));
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        await cut.Find("button.bf-form-card__open").ClickAsync(new MouseEventArgs());

        Assert.NotNull(opened);
        // The first card in document order is the default name-ascending sort's first entry.
        Assert.Equal("form-alpha", opened);
    }

    [Fact]
    public async Task OpenControlOnATableRowRaisesOnOpenInDesignerWithTheFormId()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        string? opened = null;
        var cut = Render<FormLibrary>(p => p.Add(l => l.OnOpenInDesigner, (string id) => opened = id));
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        await cut.FindAll("button.bf-library__view-button")[1].ClickAsync(new MouseEventArgs());
        await cut.FindAll("button.bf-form-table__open")[0].ClickAsync(new MouseEventArgs());

        Assert.Equal("form-alpha", opened);
    }

    [Fact]
    public async Task OpenControlsCarryAnAccessibleNameThatNamesTheForm()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        var cut = Render<FormLibrary>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        var cardOpenButton = cut.Find("button.bf-form-card__open");
        Assert.Contains("Alpha form", cardOpenButton.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResultCountIsAnnouncedPolitelyAndUpdatesWhenAFilterNarrowsTheSet()
    {
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        var cut = Render<FormLibrary>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        var resultCount = cut.Find("p.bf-library__result-count");
        Assert.Equal("polite", resultCount.GetAttribute("aria-live"));
        Assert.Equal("Showing 4 of 4 forms", resultCount.TextContent);

        await cut.Find("input[type='search']").InputAsync(new ChangeEventArgs { Value = "alpha" });

        Assert.Equal("Showing 1 of 4 forms", cut.Find("p.bf-library__result-count").TextContent);
    }

    [Fact]
    public async Task ShipsNoBlockingIssuesOnlyControlSinceASummaryCarriesNoDefinitionToLint()
    {
        // FormVersionSummary carries no FormDefinition, so a "blocking issues only" filter is
        // omitted rather than shipped as a dead control (FormLibrary's own remarks) -- this proves
        // the omission by absence, not merely that this phase never happened to click something.
        var store = await SeedFourFormsAsync();
        Services.AddSingleton(store);
        var cut = Render<FormLibrary>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.Instance.Forms.Count));

        Assert.DoesNotContain("blocking", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("input[type='checkbox']"));
    }

    /// <summary>
    /// A mutable clock, so <see cref="SeedFourFormsAsync"/> can give each publish a distinct,
    /// assertable timestamp without a mockable-clock dependency — mirrors
    /// <c>BlazeForms.Core.Tests.InMemoryStoreTests</c>'s own fixed-clock fake, but advanceable.
    /// </summary>
    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>
    /// Wraps a real <see cref="InMemoryFormDefinitionStore"/> and substitutes each listed form's
    /// <see cref="FormVersionSummary.SubmissionCount"/> from a caller-supplied lookup, purely
    /// because the real store always reports zero (it tracks no submissions of its own) — see
    /// <see cref="SeedFourFormsAsync"/>'s own remarks. Every other member delegates straight
    /// through, unchanged.
    /// </summary>
    private sealed class SubmissionCountOverrideStore(
        InMemoryFormDefinitionStore inner,
        IReadOnlyDictionary<string, int> submissionCountsByFormId) : IFormDefinitionStore
    {
        public Task<FormVersion?> GetVersionAsync(string formId, int version, CancellationToken cancellationToken = default) =>
            inner.GetVersionAsync(formId, version, cancellationToken);

        public Task<FormVersion?> GetLatestPublishedVersionAsync(string formId, CancellationToken cancellationToken = default) =>
            inner.GetLatestPublishedVersionAsync(formId, cancellationToken);

        public Task<FormVersion?> GetDraftAsync(string formId, CancellationToken cancellationToken = default) =>
            inner.GetDraftAsync(formId, cancellationToken);

        public Task SaveDraftAsync(FormVersion draft, CancellationToken cancellationToken = default) =>
            inner.SaveDraftAsync(draft, cancellationToken);

        public Task DeleteDraftAsync(string formId, CancellationToken cancellationToken = default) =>
            inner.DeleteDraftAsync(formId, cancellationToken);

        public Task<FormVersion> PublishAsync(
            string formId,
            string changeNote,
            string author,
            CancellationToken cancellationToken = default) =>
            inner.PublishAsync(formId, changeNote, author, cancellationToken);

        public Task RetireAsync(string formId, int version, CancellationToken cancellationToken = default) =>
            inner.RetireAsync(formId, version, cancellationToken);

        public Task<IReadOnlyList<FormVersionSummary>> ListVersionsAsync(
            string formId,
            CancellationToken cancellationToken = default) =>
            inner.ListVersionsAsync(formId, cancellationToken);

        public async Task<IReadOnlyList<FormVersionSummary>> ListFormsAsync(CancellationToken cancellationToken = default)
        {
            var summaries = await inner.ListFormsAsync(cancellationToken).ConfigureAwait(false);

            return [.. summaries.Select(summary => submissionCountsByFormId.TryGetValue(summary.FormId, out var count)
                ? summary with { SubmissionCount = count }
                : summary)];
        }
    }
}
