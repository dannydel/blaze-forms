using BlazeForms.Hosting;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="FormRenderer"/>'s validation triggers end to end: on blur, on page-advance,
/// and on submit — required (and <c>requiredWhenVisible</c>), the error summary's a11y model, and
/// cross-field rules (PRD §4.2, §6, §11).
/// </summary>
public sealed class FormRendererValidationTests : RendererTestContext
{
    [Fact]
    public void RequiredFieldsBlockPageAdvanceAndRenderARemedyWordedSummaryInDocumentOrder()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoRequiredFieldsDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.FindAll("button")[1].Click(); // Next, blocked: both required fields are blank.

        // Advance blocked -- still on page one.
        Assert.Equal("Page one", cut.Find("h2.bf-page__title").TextContent.Trim());

        var summary = cut.Find("[role='alert']");
        Assert.Equal("There is a problem", summary.QuerySelector("h2")!.TextContent);

        var summaryLinks = summary.QuerySelectorAll("a");
        Assert.Equal(2, summaryLinks.Length);
        Assert.Equal("Enter a value for 'First name'.", summaryLinks[0].TextContent);
        Assert.Equal("Enter a value for 'Last name'.", summaryLinks[1].TextContent);

        // Each anchor's href resolves to the exact DOM id the offending field rendered with.
        var inputs = cut.FindAll("input[type='text']");
        Assert.Equal("#" + inputs[0].GetAttribute("id"), summaryLinks[0].GetAttribute("href"));
        Assert.Equal("#" + inputs[1].GetAttribute("id"), summaryLinks[1].GetAttribute("href"));

        // The inline error is also present, next to the field it belongs to.
        Assert.Equal(2, cut.FindAll(".bf-field__error").Count);
    }

    [Fact]
    public void FailedPageAdvanceMovesFocusToTheSummary()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoRequiredFieldsDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.FindAll("button")[1].Click();

        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void FillingEveryRequiredFieldAllowsThePageToAdvance()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoRequiredFieldsDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        var inputs = cut.FindAll("input[type='text']");
        inputs[0].Input("Ada");
        cut.FindAll("input[type='text']")[1].Input("Lovelace");

        cut.FindAll("button")[1].Click();

        Assert.Equal("Page two", cut.Find("h2.bf-page__title").TextContent.Trim());
        Assert.Empty(cut.FindAll("[role='alert']"));
    }

    [Fact]
    public void OnBlurValidatesOnlyTheBlurredFieldNotEveryUntouchedField()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoRequiredFieldsDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.FindAll("input[type='text']")[0].Blur(); // first-name blurred while blank

        // Only the blurred field shows an error; last-name has not been touched, advanced past,
        // or submitted, so it shows nothing even though it would also fail right now (PRD §4.2).
        var errors = cut.FindAll(".bf-field__error");
        Assert.Single(errors);
        Assert.Equal("Enter a value for 'First name'.", errors[0].TextContent);
        Assert.Empty(cut.FindAll("[role='alert']"));
    }

    [Fact]
    public void AHiddenRequiredFieldNeverBlocksSubmitRegardlessOfWhichRequiredFlagItCarries()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RequiredWhileVisibleDefinition);
        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        // "detail" (hard Required) and "detail-two" (RequiredWhenVisible) are both hidden by
        // default -- the trigger checkbox was never checked -- so submitting immediately must
        // succeed.
        cut.FindAll("button")[1].Click();

        Assert.NotNull(captured);
        Assert.Empty(cut.FindAll("[role='alert']"));
    }

    [Fact]
    public void ARequiredFieldBlocksSubmitOnceItBecomesVisibleRegardlessOfWhichRequiredFlagItCarries()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RequiredWhileVisibleDefinition);
        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        cut.Find("input[type='checkbox']").Change(true); // reveal "detail" and "detail-two"
        cut.FindAll("button")[1].Click(); // Submit, blocked: both are visible and blank

        Assert.Null(captured);

        var summary = cut.Find("[role='alert']");
        var messages = summary.QuerySelectorAll("a").Select(a => a.TextContent).ToArray();
        Assert.Contains("Enter a value for 'Detail'.", messages);
        Assert.Contains("Enter a value for 'Detail two'.", messages);
    }

    [Fact]
    public void ACrossFieldRuleBlocksSubmitAndNavigatesToTheOffendingFieldsPage()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.CrossPageValidationDefinition);
        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        // Advance to page two (neither date is required, so this is unguarded) and give only the
        // end date -- the cross-field rule now targets "start-date", back on page one.
        cut.FindAll("button")[1].Click();
        cut.Find("input[type='date']").Change("2026-06-01");

        cut.FindAll("button")[1].Click(); // Submit, blocked by the cross-field rule

        Assert.Null(captured);
        Assert.Equal("Page one", cut.Find("h2.bf-page__title").TextContent.Trim());

        var summary = cut.Find("[role='alert']");
        Assert.Equal("Enter a start date before 'End date'.", summary.QuerySelector("a")!.TextContent);
    }

    [Fact]
    public void GroupedFieldErrorSummaryAnchorsResolveToTheGroupsOwnContainerForEveryGroupedFieldType()
    {
        // RadioGroupField/YesNoField/CheckboxGroupField/DateRangeField render no element whose id
        // is bare FieldId -- unlike a simple field, whose control is that id -- unless the group's
        // container itself carries it. Without that, the summary's #FieldId anchors point at
        // nothing (PRD §11, WCAG 2.4.1). Covers three of the four grouped types here; Radio's
        // sibling behavior is covered directly on RadioGroupField in ChoiceGroupFieldsTests.
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.GroupedRequiredFieldsDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.FindAll("button")[1].Click(); // Submit, blocked: every grouped field is required and empty.

        var summary = cut.Find("[role='alert']");
        var summaryLinks = summary.QuerySelectorAll("a");
        Assert.Equal(3, summaryLinks.Length);

        foreach (var link in summaryLinks)
        {
            var targetId = link.GetAttribute("href")!.TrimStart('#');
            Assert.NotEmpty(cut.FindAll($"#{targetId}"));
        }
    }

    [Fact]
    public void RendererChromeResolvesTheRealEnglishStringsRatherThanKeys()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoRequiredFieldsDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        Assert.Equal("Next", cut.FindAll("button")[1].TextContent.Trim());

        cut.FindAll("button")[1].Click();

        var summary = cut.Find("[role='alert']");
        Assert.Equal("There is a problem", summary.QuerySelector("h2")!.TextContent);
        Assert.Equal("Enter a value for 'First name'.", summary.QuerySelector("a")!.TextContent);
    }

    [Fact]
    public void RendererChromeStillResolvesRealStringsWhenTheHostsGlobalResourcesPathWouldMissForADiLocalizer()
    {
        // A host that calls AddLocalization(o => o.ResourcesPath = "Resources") shifts the base
        // name every DI-resolved IStringLocalizer<T> in the process falls back to, this renderer's
        // chrome strings included -- so registering that here, and registering no working
        // IStringLocalizer<RendererStrings> at all, reproduces the exact host misconfiguration
        // that used to make FormRenderer render its resource keys verbatim ("PreviousButtonLabel").
        // The fix means this bUnit context's DI state is irrelevant to the renderer's chrome now.
        using var context = new BunitContext();
        context.Services.AddLocalization(options => options.ResourcesPath = "Resources");

        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoRequiredFieldsDefinition);
        var cut = context.Render<FormRenderer>(p => p.Add(f => f.Version, version));

        Assert.Equal("Next", cut.FindAll("button")[1].TextContent.Trim());

        cut.FindAll("button")[1].Click();

        var summary = cut.Find("[role='alert']");
        Assert.Equal("There is a problem", summary.QuerySelector("h2")!.TextContent);
        Assert.Equal("Enter a value for 'First name'.", summary.QuerySelector("a")!.TextContent);
    }
}
