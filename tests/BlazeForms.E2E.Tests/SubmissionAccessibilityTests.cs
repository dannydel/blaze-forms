using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Drives a full fill through <c>/fill</c> to a real submission, then axe-scans the
/// <c>/submission/{id}</c> confirmation the respondent lands on (PRD §4.3, §11, §14 #3;
/// AGENTS.md invariant #4).
/// </summary>
public sealed class SubmissionAccessibilityTests : E2ETestBase
{
    [SuppressMessage(
        "Design",
        "CA1062:Validate arguments of public methods",
        Justification = "Both parameters are the collection fixtures xUnit itself supplies via ICollectionFixture<T> -- never null in practice -- and a base(...) initializer runs before any guard this body could add, so an in-body check here would be advisory only.")]
    public SubmissionAccessibilityTests(SampleAppFixture sampleApp, BrowserFixture browserFixture)
        : base(sampleApp, browserFixture)
    {
    }

    [Fact]
    public async Task TheSubmissionViewAfterACompleteFillHasNoAccessibilityViolations()
    {
        await SampleFormDriver.GotoFillAsync(Page, BaseUrl);
        await SampleFormDriver.CompleteApplicantInformationPageAsync(Page);
        await SampleFormDriver.CompleteCoverageSelectionPageAsync(Page);
        await SampleFormDriver.CompleteReviewPageAsync(Page);

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit" }).ClickAsync();

        // FormRenderer's OnSubmitted handler (samples/BlazeForms.Sample/Components/Pages/Fill.razor)
        // navigates to /submission/{id} once the sink accepts the envelope.
        await Page.WaitForURLAsync(new Regex(@"/submission/[^/]+$"));
        await Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Submission received" }).WaitForAsync();

        await AccessibilityAssertions.AssertNoViolationsAsync(Page, "/submission/{id} after a complete fill");
    }
}
