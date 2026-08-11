using System.Text.Json;
using BlazeForms.Hosting;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="FormRenderer"/>'s submission path (PRD §4.2, §9): the envelope's shape,
/// hidden-by-logic answers being absent rather than null, and the three ways a completed fill
/// reaches the host — <see cref="FormRenderer.OnSubmitted"/>, a registered
/// <see cref="IFormSubmissionSink"/>, and the confirmation screen.
/// </summary>
public sealed class FormRendererSubmissionTests : RendererTestContext
{
    private sealed class RecordingSink : IFormSubmissionSink
    {
        public FormSubmissionEnvelope? Received { get; private set; }

        public Task SubmitAsync(FormSubmissionEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Received = envelope;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void TheEnvelopeCarriesTheExpectedIdentityAndTimestamps()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        FormSubmissionEnvelope? captured = null;
        var beforeRender = DateTimeOffset.UtcNow;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        cut.FindAll("button")[1].Click(); // Submit -- nothing here is required.

        Assert.NotNull(captured);
        Assert.StartsWith("sub-", captured.SubmissionId, StringComparison.Ordinal);
        Assert.Equal(version.FormId, captured.FormId);
        Assert.Equal(version.Version, captured.DefinitionVersion);
        Assert.InRange(captured.StartedAt, beforeRender.AddSeconds(-1), DateTimeOffset.UtcNow);
        Assert.InRange(captured.SubmittedAt, captured.StartedAt, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void HiddenFieldsAreAbsentFromTheEnvelopeEvenWithAStaleAnswerFromBeforeTheyHid()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        // Reveal "extra", answer it, then hide it again -- FilterToVisible must drop the stale
        // answer rather than let it leak into the envelope (PRD §6, §9).
        cut.Find("input[type='checkbox']").Change(true);
        cut.Find("input[type='text']").Input("a stale answer");
        cut.Find("input[type='checkbox']").Change(false);

        cut.FindAll("button")[1].Click();

        Assert.NotNull(captured);
        Assert.False(captured.Values.ContainsKey("extra"), "A hidden field's answer must be absent, not null.");
    }

    [Fact]
    public void AVisibleFieldNeverTypedIntoIsAbsentJustLikeFilterToVisibleLeavesIt()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        cut.FindAll("button")[1].Click(); // "name" is visible throughout but never typed into.

        Assert.NotNull(captured);
        // Untouched means the renderer never wrote a key into its answer dictionary at all --
        // VisibilityEvaluator.FilterToVisible only restricts an existing dictionary, it never
        // materializes missing keys, so an untouched visible field is absent, exactly like a
        // hidden one (the two cases are indistinguishable in the envelope, by design).
        Assert.False(captured.Values.ContainsKey("name"));
    }

    [Fact]
    public void AVisibleFieldTypedIntoAndThenClearedIsPresentAsJsonNull()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        cut.Find("input[type='text']").Input("Ada");
        cut.Find("input[type='text']").Input(""); // TextField reports an empty string as null.

        cut.FindAll("button")[1].Click();

        Assert.NotNull(captured);
        // Once touched, "name" has a key -- unlike the never-touched case above -- and its
        // cleared answer round-trips as an explicit JSON null, not an absent key.
        Assert.True(captured.Values.TryGetValue("name", out var nameValue));
        Assert.Equal(JsonValueKind.Null, nameValue.ValueKind);
    }

    [Fact]
    public void ARegisteredSubmissionSinkAlsoReceivesTheEnvelope()
    {
        var sink = new RecordingSink();
        Services.AddSingleton<IFormSubmissionSink>(sink);
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        cut.FindAll("button")[1].Click();

        Assert.NotNull(captured);
        Assert.Same(captured, sink.Received);
    }

    [Fact]
    public void ConfirmationTemplateRendersInPlaceOfTheFormAndReceivesTheEnvelope()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.ConfirmationTemplate, envelope => builder => builder.AddContent(0, $"Reference: {envelope.SubmissionId}")));

        cut.FindAll("button")[1].Click();

        Assert.Contains("Reference: sub-", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button"));
        Assert.Empty(cut.FindAll(".bf-confirmation"));
    }

    [Fact]
    public void DefaultConfirmationRendersWhenNoTemplateIsProvided()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.FindAll("button")[1].Click();

        var confirmation = cut.Find(".bf-confirmation");
        Assert.Equal("status", confirmation.GetAttribute("role"));
        Assert.Equal("Thank you. Your submission has been received.", confirmation.TextContent.Trim());
    }

    [Fact]
    public void SuccessfulSubmitMovesFocusToTheDefaultConfirmation()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.FindAll("button")[1].Click();

        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task ReenteringSubmitAsyncWhileAPriorCallIsStillInFlightFiresOnSubmittedOnlyOnce()
    {
        // Models a fast double-click: the browser delivers a second onclick dispatch before the
        // first call's guard-setting statement has had a chance to make its way back to the DOM
        // as a re-render that removes the Submit button (PRD §4.2). Calling SubmitAsync directly,
        // twice, without awaiting the first in between, reproduces exactly that race
        // deterministically -- FormRenderer.SubmitAsync's remarks explain why this is more
        // reliable than dispatching two real DOM clicks on the same (about-to-vanish) button.
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.SubmissionDefinition);
        var submittedCount = 0;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => submittedCount++));

        var firstCall = cut.Instance.SubmitAsync();
        var secondCall = cut.Instance.SubmitAsync();
        await Task.WhenAll(firstCall, secondCall);

        Assert.Equal(1, submittedCount);
    }
}
