using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Serialization;
using BlazeForms.Versioning;

namespace BlazeForms.Core.Tests;

/// <summary>
/// The in-memory contracts ship for demos and tests (PRD §9); they are also the reference
/// behaviour every host implementation is expected to match.
/// </summary>
public sealed class InMemoryStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private static InMemoryFormDefinitionStore NewStore() =>
        new(new FakeTimeProvider(Now));

    private static FormDefinition Definition(string id = "form-a", string name = "Form A") =>
        new() { Id = id, Name = name };

    [Fact]
    public async Task ADraftRoundTripsThroughTheStore()
    {
        var store = NewStore();
        var draft = FormLifecycle.CreateDraft(Definition());

        await store.SaveDraftAsync(draft);
        var loaded = await store.GetDraftAsync("form-a");

        Assert.NotNull(loaded);
        Assert.Equal(FormLifecycleState.Draft, loaded!.State);
        Assert.Equal("Form A", loaded.Definition.Name);
    }

    [Fact]
    public async Task AnUnknownFormYieldsNothing()
    {
        var store = NewStore();

        Assert.Null(await store.GetDraftAsync("nope"));
        Assert.Null(await store.GetVersionAsync("nope", 1));
        Assert.Null(await store.GetLatestPublishedVersionAsync("nope"));
        Assert.Empty(await store.ListVersionsAsync("nope"));
        Assert.Empty(await store.ListFormsAsync());
    }

    [Fact]
    public async Task PublishingNumbersVersionsFromOneAndClearsTheDraft()
    {
        var store = NewStore();
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition()));

        var first = await store.PublishAsync("form-a", "Initial release.", "ada");

        Assert.Equal(1, first.Version);
        Assert.Equal(FormLifecycleState.Published, first.State);
        Assert.Equal(Now, first.PublishedAt);
        Assert.Null(await store.GetDraftAsync("form-a"));

        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition(name: "Form A, revised")));
        var second = await store.PublishAsync("form-a", "Renamed.", "ada");

        Assert.Equal(2, second.Version);
        Assert.Equal("Form A, revised", second.Definition.Name);
    }

    [Fact]
    public async Task APublishedVersionIsNeverRewritten()
    {
        var store = NewStore();
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition()));
        var first = await store.PublishAsync("form-a", "Initial release.", "ada");
        var firstJson = FormJson.SerializeDefinition(first.Definition);

        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition(name: "Form A, revised")));
        await store.PublishAsync("form-a", "Renamed.", "ada");

        var reloaded = await store.GetVersionAsync("form-a", 1);

        Assert.NotNull(reloaded);
        Assert.Equal("Form A", reloaded!.Definition.Name);
        Assert.Equal(firstJson, FormJson.SerializeDefinition(reloaded.Definition));
    }

    [Fact]
    public async Task PublishingNeedsADraftAndTheStoreOnlyAcceptsDrafts()
    {
        var store = NewStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PublishAsync("form-a", "Note.", "ada"));

        var published = FormLifecycle.Publish(
            FormLifecycle.CreateDraft(Definition()), 1, "Note.", "ada", Now);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveDraftAsync(published));
    }

    [Fact]
    public async Task TheLatestPublishedVersionIsTheHighestNumberedOne()
    {
        var store = NewStore();

        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition()));
        await store.PublishAsync("form-a", "v1.", "ada");
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition()));
        await store.PublishAsync("form-a", "v2.", "ada");

        var latest = await store.GetLatestPublishedVersionAsync("form-a");

        Assert.Equal(2, latest!.Version);
    }

    [Fact]
    public async Task RetiringStopsNewFillsButKeepsTheVersionReadable()
    {
        var store = NewStore();
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition()));
        await store.PublishAsync("form-a", "v1.", "ada");

        await store.RetireAsync("form-a", 1);

        var retired = await store.GetVersionAsync("form-a", 1);
        Assert.Equal(FormLifecycleState.Retired, retired!.State);
        Assert.Equal(Now, retired.RetiredAt);
        Assert.Null(await store.GetLatestPublishedVersionAsync("form-a"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RetireAsync("form-a", 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RetireAsync("form-a", 7));
    }

    [Fact]
    public async Task VersionHistoryListsEveryPublishedVersionInOrder()
    {
        var store = NewStore();

        for (var version = 1; version <= 3; version++)
        {
            await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition()));
            await store.PublishAsync("form-a", $"Release {version}.", "ada");
        }

        var history = await store.ListVersionsAsync("form-a");

        Assert.Equal([1, 2, 3], history.Select(summary => summary.Version));
        Assert.Equal("Release 2.", history[1].ChangeNote);
        Assert.Equal("Form A", history[0].Name);
        Assert.All(history, summary => Assert.Equal("ada", summary.Author));
    }

    [Fact]
    public async Task ListingFormsReportsOneSummaryPerFormUsingItsNewestVersion()
    {
        var store = NewStore();

        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition("form-a", "Form A")));
        await store.PublishAsync("form-a", "v1.", "ada");
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition("form-b", "Form B")));

        var forms = await store.ListFormsAsync();

        Assert.Equal(2, forms.Count);
        Assert.Equal(["form-a", "form-b"], forms.Select(summary => summary.FormId).Order(StringComparer.Ordinal));
        Assert.Equal(FormLifecycleState.Draft, forms.Single(summary => summary.FormId == "form-b").State);
        Assert.Equal(FormLifecycleState.Published, forms.Single(summary => summary.FormId == "form-a").State);
    }

    [Fact]
    public async Task DeletingTheWorkingDraftDiscardsUnpublishedEditsAndLeavesVersionsAlone()
    {
        var store = NewStore();
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition()));

        await store.DeleteDraftAsync("form-a");
        Assert.Null(await store.GetDraftAsync("form-a"));

        // A published history does not pin the working draft in place: PRD §7's "only
        // never-published drafts can be deleted" is about versions, and a working draft has by
        // definition never been published.
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition()));
        await store.PublishAsync("form-a", "v1.", "ada");
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition(name: "Abandoned edits")));

        await store.DeleteDraftAsync("form-a");

        Assert.Null(await store.GetDraftAsync("form-a"));
        Assert.Equal("Form A", (await store.GetVersionAsync("form-a", 1))!.Definition.Name);
        Assert.Single(await store.ListVersionsAsync("form-a"));
    }

    [Fact]
    public async Task DeletingAnAbsentDraftIsNotAnError()
    {
        var store = NewStore();

        await store.DeleteDraftAsync("form-a");
        await store.DeleteDraftAsync("form-a");

        Assert.Null(await store.GetDraftAsync("form-a"));
    }

    [Fact]
    public async Task RetiringTheNewestVersionLeavesNothingToFillRatherThanReopeningItsPredecessor()
    {
        var store = NewStore();

        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition()));
        await store.PublishAsync("form-a", "v1.", "ada");
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition()));
        await store.PublishAsync("form-a", "v2.", "ada");

        await store.RetireAsync("form-a", 2);

        // Falling back to v1 here would be rollback-in-place, which PRD §7 forbids.
        Assert.Null(await store.GetLatestPublishedVersionAsync("form-a"));
        Assert.Equal(FormLifecycleState.Published, (await store.GetVersionAsync("form-a", 1))!.State);
        Assert.Equal(FormLifecycleState.Retired, (await store.GetVersionAsync("form-a", 2))!.State);
    }

    [Fact]
    public async Task RetiringAnOlderVersionLeavesTheNewestFillable()
    {
        var store = NewStore();

        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition()));
        await store.PublishAsync("form-a", "v1.", "ada");
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(Definition()));
        await store.PublishAsync("form-a", "v2.", "ada");

        await store.RetireAsync("form-a", 1);

        Assert.Equal(2, (await store.GetLatestPublishedVersionAsync("form-a"))!.Version);
    }

    [Fact]
    public async Task ADraftMustAgreeWithTheDefinitionItHolds()
    {
        var store = NewStore();

        var mismatched = FormLifecycle.CreateDraft(Definition()) with { FormId = "form-b" };
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveDraftAsync(mismatched));

        var blank = FormLifecycle.CreateDraft(Definition()) with { FormId = "   " };
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveDraftAsync(blank));
    }

    [Fact]
    public async Task TheDefinitionStoreRejectsNullAndBlankIdentifiers()
    {
        var store = NewStore();

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveDraftAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.GetDraftAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => store.GetDraftAsync("  "));
    }

    [Fact]
    public async Task ADraftFillIsKeyedByFormVersionAndRespondent()
    {
        var store = new InMemoryFormDraftStore();
        var key = new FormDraftKey("form-a", 2, "respondent-1");
        var draft = new FormDraft
        {
            Key = key,
            StartedAt = Now,
            UpdatedAt = Now,
            CurrentPageIndex = 1,
            Values = FormValues.ToJsonValues(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["node-first-name"] = "Ada",
            }),
        };

        await store.SaveAsync(draft);

        var loaded = await store.LoadAsync(key);
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.CurrentPageIndex);
        Assert.Equal("Ada", loaded.Values["node-first-name"].GetString());

        Assert.Null(await store.LoadAsync(new FormDraftKey("form-a", 3, "respondent-1")));
        Assert.Null(await store.LoadAsync(new FormDraftKey("form-a", 2, "respondent-2")));
    }

    [Fact]
    public async Task SavingAFillDraftAgainReplacesIt()
    {
        var store = new InMemoryFormDraftStore();
        var key = new FormDraftKey("form-a", 1, "respondent-1");
        var draft = new FormDraft { Key = key, StartedAt = Now, UpdatedAt = Now };

        await store.SaveAsync(draft);
        await store.SaveAsync(draft with { UpdatedAt = Now.AddMinutes(5), CurrentPageIndex = 2 });

        var loaded = await store.LoadAsync(key);

        Assert.Equal(2, loaded!.CurrentPageIndex);
        Assert.Equal(Now.AddMinutes(5), loaded.UpdatedAt);
    }

    [Fact]
    public async Task DeletingAFillDraftIsIdempotent()
    {
        var store = new InMemoryFormDraftStore();
        var key = new FormDraftKey("form-a", 1, "respondent-1");

        await store.SaveAsync(new FormDraft { Key = key, StartedAt = Now, UpdatedAt = Now });
        await store.DeleteAsync(key);
        await store.DeleteAsync(key);

        Assert.Null(await store.LoadAsync(key));
    }

    [Fact]
    public async Task TheDraftStoreRejectsNullArguments()
    {
        var store = new InMemoryFormDraftStore();

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.LoadAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.DeleteAsync(null!));
    }

    /// <summary>
    /// A fixed clock, so publish timestamps are assertable without a mockable-clock dependency.
    /// </summary>
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
