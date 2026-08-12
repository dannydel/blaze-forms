using BlazeForms.Hosting;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="FormRenderer.Ephemeral"/> (PRD §4.1): a preview fill must never resolve or
/// invoke the host's <see cref="IFormSubmissionSink"/> or <see cref="IFormDraftStore"/> — no
/// load, no autosave, no delete, no submission — while <see cref="FormRenderer.OnSubmitted"/> and
/// its default confirmation still fire exactly as they would on a real fill. A sibling
/// non-ephemeral render proves registering both fakes the normal way still reaches them, guarding
/// against this parameter accidentally short-circuiting the ordinary path too.
/// </summary>
public sealed class FormRendererEphemeralTests : RendererTestContext
{
    private sealed class RecordingSink : IFormSubmissionSink
    {
        public int SubmitCount { get; private set; }

        public Task SubmitAsync(FormSubmissionEnvelope envelope, CancellationToken cancellationToken = default)
        {
            SubmitCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDraftStore : IFormDraftStore
    {
        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public int DeleteCount { get; private set; }

        public Task<FormDraft?> LoadAsync(FormDraftKey key, CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult<FormDraft?>(null);
        }

        public Task SaveAsync(FormDraft draft, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(FormDraftKey key, CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void AnEphemeralFillNeverLoadsSavesDeletesOrSubmitsToTheHostThroughABlurPageAdvanceAndSubmit()
    {
        var sink = new RecordingSink();
        var draftStore = new RecordingDraftStore();
        Services.AddSingleton<IFormSubmissionSink>(sink);
        Services.AddSingleton<IFormDraftStore>(draftStore);

        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.Ephemeral, true)
            // A respondent key would normally be exactly what makes the draft store live -- set
            // here deliberately, so the proof is that Ephemeral itself suppresses the store, not
            // merely that no key was supplied.
            .Add(f => f.RespondentKey, "resp-preview"));

        // Reveal and fill the conditional field (exercises live logic), blur it (the ordinary
        // autosave trigger), then advance/submit -- SubmissionDefinition is a single page, so the
        // second button is Submit.
        cut.Find("input[type='checkbox']").Change(true);
        cut.Find("input[type='text'][id$='-extra']").Input("preview answer");
        cut.Find("input[type='text'][id$='-extra']").Blur();
        cut.FindAll("button")[1].Click();

        Assert.Equal(0, draftStore.LoadCount);
        Assert.Equal(0, draftStore.SaveCount);
        Assert.Equal(0, draftStore.DeleteCount);
        Assert.Equal(0, sink.SubmitCount);
    }

    [Fact]
    public void AnEphemeralFillStillFiresOnSubmittedAndShowsItsOwnDefaultConfirmation()
    {
        Services.AddSingleton<IFormSubmissionSink>(new RecordingSink());
        Services.AddSingleton<IFormDraftStore>(new RecordingDraftStore());

        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.Ephemeral, true)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        cut.FindAll("button")[1].Click();

        Assert.NotNull(captured);
        Assert.NotEmpty(cut.FindAll(".bf-confirmation"));
    }

    [Fact]
    public void ANonEphemeralFillWithTheSameRegisteredFakesStillLoadsSavesDeletesAndSubmitsNormally()
    {
        // The regression guard: Ephemeral defaults to false, and registering the exact same
        // recording fakes must still reach them the ordinary way, proving the skip above is
        // conditional on Ephemeral rather than something that broke the normal path.
        var sink = new RecordingSink();
        var draftStore = new RecordingDraftStore();
        Services.AddSingleton<IFormSubmissionSink>(sink);
        Services.AddSingleton<IFormDraftStore>(draftStore);

        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-real"));

        cut.Find("input[type='text'][id$='-name']").Input("Ada");
        cut.Find("input[type='text'][id$='-name']").Blur();
        cut.FindAll("button")[1].Click();

        Assert.Equal(1, draftStore.LoadCount);
        Assert.True(draftStore.SaveCount >= 1);
        Assert.Equal(1, draftStore.DeleteCount);
        Assert.Equal(1, sink.SubmitCount);
    }
}
