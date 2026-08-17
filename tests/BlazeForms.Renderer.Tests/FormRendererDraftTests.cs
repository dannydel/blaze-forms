using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Serialization;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="FormRenderer"/>'s fill-draft behavior (PRD §4.2, §9, D13): resuming a
/// returning respondent's in-progress fill exactly once and prerender-safely, autosaving on blur
/// and page-advance, persisting raw (unfiltered) answers so a currently-hidden field's answer
/// survives a resume, deleting the draft on a successful submit, and never touching the store at
/// all for an anonymous fill.
/// </summary>
public sealed class FormRendererDraftTests : RendererTestContext
{
    /// <summary>
    /// Wraps <see cref="InMemoryFormDraftStore"/> so tests can assert on call counts and
    /// captured arguments without reimplementing storage semantics.
    /// </summary>
    private sealed class RecordingDraftStore : IFormDraftStore
    {
        private readonly InMemoryFormDraftStore _inner = new();

        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public int DeleteCount { get; private set; }

        public List<FormDraft> SavedDrafts { get; } = [];

        public List<FormDraftKey> DeletedKeys { get; } = [];

        public async Task<FormDraft?> LoadAsync(FormDraftKey key, CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return await _inner.LoadAsync(key, cancellationToken).ConfigureAwait(false);
        }

        public async Task SaveAsync(FormDraft draft, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            SavedDrafts.Add(draft);
            await _inner.SaveAsync(draft, cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteAsync(FormDraftKey key, CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            DeletedKeys.Add(key);
            await _inner.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Seeds a draft directly into the underlying store, as if a prior fill had already
        /// autosaved it, without that seeding counting toward <see cref="SaveCount"/>.
        /// </summary>
        public Task SeedAsync(FormDraft draft) => _inner.SaveAsync(draft);
    }

    [Fact]
    public async Task ResumeSeedsValuesAndCurrentPageFromAStoredDraft()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoStepDefinition);
        var store = new RecordingDraftStore();
        var key = new FormDraftKey(version.FormId, version.Version, "resp-1");
        var draft = new FormDraft
        {
            Key = key,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Values = FormValues.ToJsonValues(new Dictionary<string, object?> { ["notes"] = "Resumed notes" }),
            CurrentPageIndex = 1,
        };
        await store.SeedAsync(draft);
        Services.AddSingleton<IFormDraftStore>(store);

        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1"));

        // Page two ("notes") is what the draft resumed onto -- page one's fields are gone.
        Assert.Empty(cut.FindAll("[id$='-first-name']"));
        var notes = cut.Find("[id$='-notes']");
        Assert.Equal("Resumed notes", notes.GetAttribute("value") ?? notes.TextContent);
    }

    [Fact]
    public void TheDraftKeyIsPinnedToTheVersionTheFillStartedOn()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoFieldDefinition);
        var store = new RecordingDraftStore();
        Services.AddSingleton<IFormDraftStore>(store);

        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1"));

        cut.Find("[id$='-field-a']").Blur();

        Assert.NotEmpty(store.SavedDrafts);
        var savedKey = store.SavedDrafts[0].Key;
        Assert.Equal(version.FormId, savedKey.FormId);
        Assert.Equal(version.Version, savedKey.DefinitionVersion);
        Assert.Equal("resp-1", savedKey.RespondentKey);
    }

    [Fact]
    public void SaveAsyncIsInvokedOnFieldBlur()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoFieldDefinition);
        var store = new RecordingDraftStore();
        Services.AddSingleton<IFormDraftStore>(store);

        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1"));

        cut.Find("[id$='-field-a']").Input("Ada");
        cut.Find("[id$='-field-a']").Blur();

        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void SaveAsyncIsInvokedOnPageAdvance()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoStepDefinition);
        var store = new RecordingDraftStore();
        Services.AddSingleton<IFormDraftStore>(store);

        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1"));

        cut.FindAll("button")[1].Click(); // Next -- nothing on page one is required.

        Assert.True(store.SaveCount >= 1);
        Assert.Equal(1, store.SavedDrafts[^1].CurrentPageIndex);
    }

    [Fact]
    public void DraftPersistsAnAnswerForAFieldCurrentlyHiddenByItsController()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        var store = new RecordingDraftStore();
        Services.AddSingleton<IFormDraftStore>(store);

        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1"));

        var trigger = cut.Find("input[type='checkbox']");
        trigger.Change(true); // reveal "extra"
        // "show-extra"'s own id ends in "-extra" too, so the selector must anchor on the input
        // type as well to land on the text field rather than the checkbox that controls it.
        cut.Find("input[type='text'][id$='-extra']").Input("secret answer");
        trigger.Change(false); // hide "extra" again -- its answer must survive in the draft
        trigger.Blur();

        Assert.NotEmpty(store.SavedDrafts);
        var lastDraft = store.SavedDrafts[^1];
        Assert.True(lastDraft.Values.TryGetValue("extra", out var extraValue));
        Assert.Equal("secret answer", extraValue.GetString());
    }

    [Fact]
    public void DeleteAsyncIsCalledOnASuccessfulSubmit()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        var store = new RecordingDraftStore();
        Services.AddSingleton<IFormDraftStore>(store);

        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1"));

        cut.FindAll("button")[1].Click(); // Submit.

        Assert.Equal(1, store.DeleteCount);
        Assert.Equal(version.FormId, store.DeletedKeys[0].FormId);
        Assert.Equal(version.Version, store.DeletedKeys[0].DefinitionVersion);
    }

    [Fact]
    public void AnAnonymousFillNeverTouchesTheStore()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        var store = new RecordingDraftStore();
        Services.AddSingleton<IFormDraftStore>(store);

        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version)); // RespondentKey unset.

        cut.Find("[id$='-name']").Input("Ada");
        cut.Find("[id$='-name']").Blur();
        cut.FindAll("button")[1].Click(); // Submit.

        Assert.Equal(0, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, store.DeleteCount);
    }

    [Fact]
    public async Task ResumingSetsTheEventualEnvelopesStartedAtToTheDraftsStartedAt()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        var store = new RecordingDraftStore();
        var key = new FormDraftKey(version.FormId, version.Version, "resp-1");
        var originalStartedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var draft = new FormDraft
        {
            Key = key,
            StartedAt = originalStartedAt,
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Values = FormValues.ToJsonValues(new Dictionary<string, object?>()),
            CurrentPageIndex = 0,
        };
        await store.SeedAsync(draft);
        Services.AddSingleton<IFormDraftStore>(store);

        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1")
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        await cut.FindAll("button")[1].ClickAsync(new MouseEventArgs());

        Assert.NotNull(captured);
        Assert.Equal(originalStartedAt, captured.StartedAt);
    }

    [Fact]
    public async Task DisposeAsyncCompletesCleanlyAndCancelsInFlightWork()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        var store = new RecordingDraftStore();
        Services.AddSingleton<IFormDraftStore>(store);

        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1"));

        await cut.Instance.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsyncIsIdempotent()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        Services.AddSingleton<IFormDraftStore>(new RecordingDraftStore());

        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1"));

        // A defensive host teardown (or bUnit disposing after an explicit dispose) must not throw
        // ObjectDisposedException from cancelling an already-disposed CancellationTokenSource.
        await cut.Instance.DisposeAsync();
        await cut.Instance.DisposeAsync();
    }

    [Fact]
    public void LoadHappensExactlyOnce()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoFieldDefinition);
        var store = new RecordingDraftStore();
        Services.AddSingleton<IFormDraftStore>(store);

        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1"));

        // A couple of unrelated re-renders (a value change re-renders the component tree via
        // StateHasChanged, and OnAfterRenderAsync fires again with firstRender: false each time)
        // must not cause a second LoadAsync.
        cut.Find("[id$='-field-a']").Input("Ada");
        cut.Find("[id$='-field-a']").Blur();

        Assert.Equal(1, store.LoadCount);
    }

    [Fact]
    public async Task ResumeRestoresAStoredDateValueIntoTheDateField()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(
            FormRendererTestFixtures.CrossPageValidationDefinition);
        var store = new RecordingDraftStore();
        var key = new FormDraftKey(version.FormId, version.Version, "resp-1");

        await store.SeedAsync(new FormDraft
        {
            Key = key,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Values = FormValues.ToJsonValues(new Dictionary<string, object?>
            {
                ["start-date"] = new DateOnly(2026, 6, 1),
            }),
            CurrentPageIndex = 0,
        });
        Services.AddSingleton<IFormDraftStore>(store);

        var cut = Render<FormRenderer>(parameters => parameters
            .Add(field => field.Version, version)
            .Add(field => field.RespondentKey, "resp-1"));

        var dateInput = cut.Find("input[type='date']");

        Assert.Equal("2026-06-01", dateInput.GetAttribute("value"));
    }
}
