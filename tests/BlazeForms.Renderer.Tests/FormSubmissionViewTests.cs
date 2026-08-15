using System.Globalization;
using BlazeForms.Definitions;
using Bunit;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="FormSubmissionView"/>'s read-only rendering rules (PRD §4.3): sectioning
/// exactly as the captured definition, telling a fill-time-hidden field apart from a
/// visible-but-empty one by re-evaluating visibility rather than by key presence, choice
/// value-to-label resolution, the superseded-version notice, and PRD success criterion #5 (a
/// captured submission renders identically regardless of what has since published).
/// </summary>
public sealed class FormSubmissionViewTests : RendererTestContext
{
    private static List<string> Terms(IRenderedComponent<FormSubmissionView> cut) =>
        cut.FindAll("dt").Select(e => e.TextContent).ToList();

    private static List<string> ValueCells(IRenderedComponent<FormSubmissionView> cut) =>
        cut.FindAll("dd").Select(e => e.TextContent).ToList();

    private static string ValueFor(IRenderedComponent<FormSubmissionView> cut, string label)
    {
        var index = Terms(cut).IndexOf(label);
        Assert.True(index >= 0, $"No row labelled '{label}' was rendered.");
        return ValueCells(cut)[index];
    }

    [Fact]
    public void RendersLabelValueRowsSectionedExactlyAsTheCapturedDefinition()
    {
        var version = FormSubmissionViewTestFixtures.ToVersion(FormSubmissionViewTestFixtures.SubmissionViewDefinition);
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelope(
            FormSubmissionViewTestFixtures.SubmissionViewDefinition,
            version.Version,
            FormSubmissionViewTestFixtures.SubmissionViewValues);

        var cut = Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version));

        var pageHeadings = cut.FindAll("h2").Select(e => e.TextContent).ToList();
        Assert.Equal(["About you", "Notes"], pageHeadings);

        var sectionHeadings = cut.FindAll("h3").Select(e => e.TextContent).ToList();
        Assert.Equal(["Your details", "Additional notes"], sectionHeadings);

        Assert.Equal("Ada Lovelace", ValueFor(cut, "Name"));
    }

    [Fact]
    public void AnUntitledPageRendersAPositionalFallbackHeadingRatherThanAnEmptyOne()
    {
        var version = FormSubmissionViewTestFixtures.ToVersion(FormSubmissionViewTestFixtures.NullPageTitleDefinition);
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelope(
            FormSubmissionViewTestFixtures.NullPageTitleDefinition,
            version.Version,
            new Dictionary<string, object?>(StringComparer.Ordinal));

        var cut = Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version));

        // No axe empty-heading violation, and the enclosing section keeps a non-empty
        // accessible name (AGENTS.md invariant #4) -- FormPage.Title is null, but the h2 must
        // never itself be empty.
        var heading = cut.Find("h2");
        Assert.False(string.IsNullOrWhiteSpace(heading.TextContent));
        Assert.Equal("Page 1", heading.TextContent);

        var section = cut.Find("section.bf-submission__page");
        var labelledBy = section.GetAttribute("aria-labelledby");
        Assert.False(string.IsNullOrWhiteSpace(labelledBy));
        Assert.Equal(heading.Id, labelledBy);
    }

    [Fact]
    public void AFieldHiddenByLogicAtFillTimeShowsTheNotApplicableTextRatherThanEmpty()
    {
        var version = FormSubmissionViewTestFixtures.ToVersion(FormSubmissionViewTestFixtures.SubmissionViewDefinition);
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelope(
            FormSubmissionViewTestFixtures.SubmissionViewDefinition,
            version.Version,
            FormSubmissionViewTestFixtures.SubmissionViewValues);

        var cut = Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version));

        // "secret" is absent from the envelope *and* re-evaluates as hidden, because "show-extra"
        // was never answered true -- the "Not applicable" text, not the empty placeholder, is the
        // only correct reading of that combination (PRD §4.3, §9).
        Assert.Equal("Not applicable — hidden by logic at fill time", ValueFor(cut, "Secret detail"));
    }

    [Fact]
    public void AFieldVisibleAtFillTimeButLeftEmptyShowsTheEmDashNotNotApplicable()
    {
        var version = FormSubmissionViewTestFixtures.ToVersion(FormSubmissionViewTestFixtures.SubmissionViewDefinition);
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelope(
            FormSubmissionViewTestFixtures.SubmissionViewDefinition,
            version.Version,
            FormSubmissionViewTestFixtures.SubmissionViewValues);

        var cut = Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version));

        // "notes" carries no VisibleWhen at all -- it was visible for the whole fill -- and
        // "show-extra" is unconditionally visible too; both are simply absent from the envelope
        // because the respondent never touched them. Absent-but-visible must render the empty
        // placeholder, never "Not applicable" (PRD §4.3, §9 maintainer decision).
        Assert.Equal("—", ValueFor(cut, "Notes"));
        Assert.Equal("—", ValueFor(cut, "Show extra?"));
    }

    [Fact]
    public void AFieldHiddenByItsControllerIsFilteredFromTheEnvelopeAndReadsNotApplicableNotItsStaleAnswer()
    {
        // Unlike the tests above, which start from BuildEnvelope's already-clean value set, this
        // one reconstructs the envelope the way FormRenderer.BuildSubmissionEnvelope really does:
        // running VisibilityEvaluator.FilterToVisible against a *raw* fill-time value set that
        // still carries "secret"'s answer even though "show-extra" (its controller) is false --
        // exactly the a→b→c leak this component's whole hidden-vs-empty distinction exists to
        // prevent.
        var version = FormSubmissionViewTestFixtures.ToVersion(FormSubmissionViewTestFixtures.SubmissionViewDefinition);
        var rawValues = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = "Ada Lovelace",
            ["show-extra"] = false,
            ["secret"] = "x",
        };
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelopeFromRawValues(
            FormSubmissionViewTestFixtures.SubmissionViewDefinition,
            version.Version,
            rawValues);

        var cut = Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version));

        var value = ValueFor(cut, "Secret detail");
        Assert.Equal("Not applicable — hidden by logic at fill time", value);
        Assert.DoesNotContain("x", value, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldVisibleAtFillTimeButNeverAnsweredReadsTheEmDashViaTheRealFilterPathToo()
    {
        // The positive counterpart to the test above, through the same real FilterToVisible
        // path: "show-extra" is true, so "secret"'s controller genuinely shows it, but the
        // respondent left it untouched -- visible-and-unanswered must still read the em-dash
        // placeholder, never "Not applicable".
        var version = FormSubmissionViewTestFixtures.ToVersion(FormSubmissionViewTestFixtures.SubmissionViewDefinition);
        var rawValues = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = "Ada Lovelace",
            ["show-extra"] = true,
        };
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelopeFromRawValues(
            FormSubmissionViewTestFixtures.SubmissionViewDefinition,
            version.Version,
            rawValues);

        var cut = Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version));

        Assert.Equal("—", ValueFor(cut, "Secret detail"));
    }

    [Fact]
    public void AChoiceValueRendersItsOptionLabelAndACheckboxGroupListsEverySelectedLabel()
    {
        var version = FormSubmissionViewTestFixtures.ToVersion(FormSubmissionViewTestFixtures.SubmissionViewDefinition);
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelope(
            FormSubmissionViewTestFixtures.SubmissionViewDefinition,
            version.Version,
            FormSubmissionViewTestFixtures.SubmissionViewValues);

        var cut = Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version));

        // The envelope stores the option Value ("wa"/"a"/"b") -- the row must show the Label
        // (AGENTS.md invariant #5), never the raw stored value.
        Assert.Equal("Washington", ValueFor(cut, "State"));
        Assert.Equal("Topic A, Topic B", ValueFor(cut, "Topics"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    [InlineData(2)]
    public void NoSupersededNoticeAppearsWhenLatestIsNullOrNoGreaterThanTheCapturedVersion(int? latest)
    {
        var version = FormSubmissionViewTestFixtures.ToVersion(FormSubmissionViewTestFixtures.SubmissionViewDefinition, version: 3);
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelope(
            FormSubmissionViewTestFixtures.SubmissionViewDefinition,
            version.Version,
            FormSubmissionViewTestFixtures.SubmissionViewValues);

        var cut = Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version)
            .Add(f => f.LatestPublishedVersion, latest));

        Assert.Empty(cut.FindAll("[role='note']"));
    }

    [Fact]
    public void ASupersededNoticeAppearsAndIsProgrammaticallyLabelledWhenLatestIsGreater()
    {
        var version = FormSubmissionViewTestFixtures.ToVersion(FormSubmissionViewTestFixtures.SubmissionViewDefinition, version: 3);
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelope(
            FormSubmissionViewTestFixtures.SubmissionViewDefinition,
            version.Version,
            FormSubmissionViewTestFixtures.SubmissionViewValues);

        var cut = Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version)
            .Add(f => f.LatestPublishedVersion, 4));

        var notice = cut.Find("[role='note']");
        Assert.False(string.IsNullOrWhiteSpace(notice.GetAttribute("aria-label")));
        Assert.Contains("v4", notice.TextContent, StringComparison.Ordinal);
        Assert.Contains("v3", notice.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameSubmissionRendersIdenticalValueRowsRegardlessOfLatestPublishedVersion()
    {
        // PRD success criterion #5: a submission captured against v3 renders identically after
        // v4 publishes. Toggling only LatestPublishedVersion here is deliberate, not a gap: this
        // component takes its rendered Version as a parameter and structurally always renders
        // against exactly that captured definition, never against whatever a host's store
        // considers "latest" -- there is no code path by which LatestPublishedVersion could reach
        // the value rows at all, so no amount of additional toggling of it would exercise
        // anything this criterion is actually about.
        var version = FormSubmissionViewTestFixtures.ToVersion(FormSubmissionViewTestFixtures.SubmissionViewDefinition, version: 3);
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelope(
            FormSubmissionViewTestFixtures.SubmissionViewDefinition,
            version.Version,
            FormSubmissionViewTestFixtures.SubmissionViewValues);

        var beforePublish = Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version)
            .Add(f => f.LatestPublishedVersion, (int?)null));

        var afterPublish = Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version)
            .Add(f => f.LatestPublishedVersion, 4));

        var rowsBefore = string.Join('|', Terms(beforePublish).Zip(ValueCells(beforePublish), (t, v) => $"{t}={v}"));
        var rowsAfter = string.Join('|', Terms(afterPublish).Zip(ValueCells(afterPublish), (t, v) => $"{t}={v}"));

        Assert.Equal(rowsBefore, rowsAfter);
    }

    [Fact]
    public void ACapturedHeadingRendersOutsideItsDlAndTheDlHasOnlyDtDdChildren()
    {
        var version = FormSubmissionViewTestFixtures.ToVersion(FormSubmissionViewTestFixtures.HeadingGroupedDefinition);
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelope(
            FormSubmissionViewTestFixtures.HeadingGroupedDefinition,
            version.Version,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["before"] = "b", ["after-h1"] = "a" });

        var cut = Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version));

        // Every <dl> in the document must contain only dt/dd elements -- a captured heading's
        // div must never be a direct child of one (a <dl>'s only valid children are dt/dd
        // groups, optionally wrapped in a div that itself contains only dt/dd).
        foreach (var list in cut.FindAll("dl"))
        {
            Assert.All(list.Children, child => Assert.True(child.TagName is "DT" or "DD"));
        }

        // Both headings still render, in order, as siblings above their runs -- "Trailing"
        // introduces no fields, so no empty <dl> follows it.
        var subheadings = cut.FindAll(".bf-submission__subheading").Select(e => e.TextContent).ToList();
        Assert.Equal(["Intro", "Trailing"], subheadings);

        Assert.Equal(2, cut.FindAll("dl").Count);
        Assert.Equal("b", ValueFor(cut, "Before"));
        Assert.Equal("a", ValueFor(cut, "After intro"));
    }

    [Fact]
    public void ACapturedCalcValueRendersFormattedPerItsCalcFormatRatherThanThePlaceholder()
    {
        var version = FormSubmissionViewTestFixtures.ToVersion(FormSubmissionViewTestFixtures.CalcDefinition);
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelope(
            FormSubmissionViewTestFixtures.CalcDefinition,
            version.Version,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["total"] = 42.5m });

        var cut = Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version));

        // "total" is captured as a plain number in the envelope -- the row must format it per the
        // node's own CalcFormat (Currency here: current-culture, two decimals, no symbol), not
        // fall back to "Not yet calculated".
        Assert.Equal(42.5m.ToString("F2", CultureInfo.CurrentCulture), ValueFor(cut, "Total"));
    }

    [Fact]
    public void ALegacyEnvelopeWithNoCapturedCalcValueFallsBackToThePlaceholder()
    {
        var version = FormSubmissionViewTestFixtures.ToVersion(FormSubmissionViewTestFixtures.CalcDefinition);
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelope(
            FormSubmissionViewTestFixtures.CalcDefinition,
            version.Version,
            new Dictionary<string, object?>(StringComparer.Ordinal));

        var cut = Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version));

        // A submission captured before the calc engine existed (or one the engine could not
        // resolve) carries no "total" key at all -- the row must read the author's own
        // placeholder, exactly like the live renderer's CalcField does for the same case.
        Assert.Equal("Not yet calculated", ValueFor(cut, "Total"));
    }

    [Fact]
    public void DateValuesFormatForDisplayUsingCurrentCultureRatherThanAlwaysUsStyle()
    {
        var definition = new FormDefinition
        {
            Id = "form-date-culture",
            Name = "Date culture",
            Pages =
            [
                new FormPage
                {
                    Id = "page-1",
                    Title = "Page one",
                    Sections =
                    [
                        new FormSection
                        {
                            Id = "section-1",
                            Nodes = [new FormNode { Id = "when", Type = NodeType.Date, Label = "When" }],
                        },
                    ],
                },
            ],
        };
        var version = FormSubmissionViewTestFixtures.ToVersion(definition);
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelope(
            definition,
            version.Version,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["when"] = "2026-03-05" });

        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            // German short-date order (dd.MM.yyyy) reads unambiguously differently from the
            // US-style MM/dd/yyyy the pre-fix InvariantCulture formatting always produced --
            // proof the display path now actually follows CurrentCulture rather than being
            // pinned to one culture regardless of the reviewer's own.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var cut = Render<FormSubmissionView>(p => p
                .Add(f => f.Envelope, envelope)
                .Add(f => f.Version, version));

            Assert.Equal("05.03.2026", ValueFor(cut, "When"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
