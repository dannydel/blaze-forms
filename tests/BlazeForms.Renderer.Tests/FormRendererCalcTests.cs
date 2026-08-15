using System.Globalization;
using System.Text.Json;
using BlazeForms.Fields;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Serialization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="FormRenderer"/>'s Increment B wiring of the calc evaluation engine
/// (calc-engine-plan.md §D-D, §D-E): recomputing on every answer change and draft resume,
/// capturing a visible calc's value into the submission envelope exactly like any other answer,
/// letting a calc's value feed a visibility rule, resolving <c>today()</c> deterministically
/// through an injected <see cref="TimeProvider"/>, and preserving render discipline for fields a
/// calculation does not depend on.
/// </summary>
public sealed class FormRendererCalcTests : RendererTestContext
{
    /// <summary>
    /// A <see cref="TimeProvider"/> pinned to one instant, with its local time zone fixed to UTC
    /// so <see cref="TimeProvider.GetLocalNow"/> never depends on the machine running the test.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public void ChangingADependencyFieldRecomputesTheCalcsDisplayedValue()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.CalcDependsOnOneOfTwoFieldsDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        var total = cut.Find("[id$='-total']");
        Assert.Equal("", total.TextContent);

        cut.Find("[id$='-a']").Input("5");

        total = cut.Find("[id$='-total']");
        Assert.Equal("5", total.TextContent);

        cut.Find("[id$='-a']").Input("9");

        total = cut.Find("[id$='-total']");
        Assert.Equal("9", total.TextContent);
    }

    [Fact]
    public void AVisibleCalcsComputedValueIsCapturedInTheEnvelopeAsANumber()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.CalcDependsOnOneOfTwoFieldsDefinition);
        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        cut.Find("[id$='-a']").Input("5");
        cut.FindAll("button")[1].Click(); // Submit -- nothing here is required.

        Assert.NotNull(captured);
        Assert.True(captured.Values.TryGetValue("total", out var totalValue));
        Assert.Equal(JsonValueKind.Number, totalValue.ValueKind);
        Assert.Equal(5m, totalValue.GetDecimal());
    }

    [Fact]
    public void AHiddenCalcIsAbsentFromTheEnvelope()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.CalcHiddenByVisibilityDefinition);
        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        // "trigger" is never checked -- "hidden-total" stays hidden for the whole fill even
        // though FormRenderer.RecomputeCalculations still computes its value internally.
        cut.FindAll("button")[1].Click();

        Assert.NotNull(captured);
        Assert.False(captured.Values.ContainsKey("hidden-total"), "A hidden calc's value must be absent, not null.");
    }

    [Fact]
    public void AVisibilityRuleCanTargetACalcNodesComputedValue()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.CalcFeedsVisibilityDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        // "total" starts blank (no amount typed yet), so "total > 100" is false and the notice
        // stays hidden.
        Assert.Empty(cut.FindAll("[id$='-bonus-notice']"));

        cut.Find("[id$='-amount']").Input("150");

        // "total" (= amount) now computes to 150, which the notice's own VisibleWhen reads
        // straight out of the answer dictionary RecomputeCalculations wrote it into -- no special
        // wiring beyond what any other field's VisibleWhen already gets.
        Assert.NotEmpty(cut.FindAll("[id$='-bonus-notice']"));

        cut.Find("[id$='-amount']").Input("50");

        Assert.Empty(cut.FindAll("[id$='-bonus-notice']"));
    }

    [Fact]
    public async Task ResumingADraftRecomputesADependentCalc()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.CalcDependsOnOneOfTwoFieldsDefinition);
        var store = new InMemoryFormDraftStore();
        var key = new FormDraftKey(version.FormId, version.Version, "resp-1");
        var draft = new FormDraft
        {
            Key = key,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Values = FormValues.ToJsonValues(new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = 7m }),
            CurrentPageIndex = 0,
        };
        await store.SaveAsync(draft);
        Services.AddSingleton<IFormDraftStore>(store);

        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1"));

        // The resumed answer to "a" is not re-typed -- RecomputeCalculations at the end of
        // LoadDraftAsync is what puts "total" at 7 before the respondent does anything else.
        var total = cut.Find("[id$='-total']");
        Assert.Equal("7", total.TextContent);
    }

    [Fact]
    public void ATodayOnlyCalcResolvesDeterministicallyThroughTheInjectedTimeProvider()
    {
        var fixedNow = new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero);
        Services.AddSingleton<TimeProvider>(new FixedTimeProvider(fixedNow));

        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.CalcTodayOnlyDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        // Already resolved on the very first render (OnInitialized calls RecomputeCalculations
        // once) -- nothing needs typing into for a today()-only expression to have a value.
        var expected = new DateOnly(2026, 3, 5).ToString("d", CultureInfo.CurrentCulture);
        var output = cut.Find("[id$='-today-date']");
        Assert.Equal(expected, output.TextContent);
    }

    [Fact]
    public void TypingInAFieldTheCalcDoesNotDependOnNeverRecomputesOrReRendersIt()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.CalcDependsOnOneOfTwoFieldsDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        var calcField = cut.FindComponent<CalcField>();
        var rendersBefore = calcField.RenderCount;

        cut.Find("[id$='-b']").Input("123");

        Assert.Equal(rendersBefore, calcField.RenderCount);
        Assert.Equal("", cut.Find("[id$='-total']").TextContent);
    }
}
