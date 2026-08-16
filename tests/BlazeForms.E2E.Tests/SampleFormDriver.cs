using Microsoft.Playwright;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Drives the reference enrollment form (<c>samples/BlazeForms.Sample/Data/EnrollmentForm.cs</c>)
/// through its three pages via accessible locators only — labels and roles, never a raw CSS id —
/// so these tests exercise the same paths a screen-reader or keyboard-only respondent would (PRD
/// §11).
/// </summary>
internal static class SampleFormDriver
{
    /// <summary>
    /// Navigates to <c>/fill</c> with a respondent key unique to this call, so no two calls ever
    /// share one autosaved draft on the collection-scoped sample host (<c>Fill.razor</c>'s
    /// <c>?respondent=</c> override), then waits for the reference form's first page to render.
    /// </summary>
    /// <returns>The respondent key this fill was given, for a caller that needs it again.</returns>
    public static async Task<string> GotoFillAsync(IPage page, string baseUrl)
    {
        var respondentKey = $"e2e-{Guid.NewGuid():n}";
        await page.GotoAsync($"{baseUrl}/fill?respondent={respondentKey}").ConfigureAwait(false);
        await page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Benefits Enrollment" })
            .WaitForAsync().ConfigureAwait(false);
        return respondentKey;
    }

    /// <summary>
    /// Fills every required field on "Applicant information" (page 1) with the minimum answers
    /// needed to advance — "No" for dependents, so the conditional "Number of dependents" branch
    /// stays hidden — and advances to page 2.
    /// </summary>
    public static async Task CompleteApplicantInformationPageAsync(IPage page)
    {
        await page.GetByLabel("Full legal name").FillAsync("Jordan Rivera").ConfigureAwait(false);
        await page.GetByLabel("Email address").FillAsync("jordan.rivera@example.com").ConfigureAwait(false);
        await page.GetByLabel("Date of birth").FillAsync("1990-05-14").ConfigureAwait(false);
        await page.GetByLabel("No", new PageGetByLabelOptions { Exact = true }).CheckAsync().ConfigureAwait(false);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next" }).ClickAsync().ConfigureAwait(false);
        await page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Coverage selection" })
            .WaitForAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Fills every required field on "Coverage selection" (page 2) — "Standard" for program
    /// type, so the conditional hardship-certification branch stays hidden — and advances to the
    /// review page.
    /// </summary>
    public static async Task CompleteCoverageSelectionPageAsync(IPage page)
    {
        await page.GetByLabel("Program type").SelectOptionAsync(new SelectOptionValue { Label = "Standard" }).ConfigureAwait(false);
        await page.GetByLabel("Email", new PageGetByLabelOptions { Exact = true }).CheckAsync().ConfigureAwait(false);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next" }).ClickAsync().ConfigureAwait(false);
        await page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Review and submit" })
            .WaitForAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Fills the review page's one required field, the requested coverage date range.
    /// </summary>
    public static async Task CompleteReviewPageAsync(IPage page)
    {
        await page.GetByLabel("Start date").FillAsync("2026-01-01").ConfigureAwait(false);
        await page.GetByLabel("End date").FillAsync("2026-12-31").ConfigureAwait(false);
    }

    /// <summary>
    /// Types <paramref name="amount"/> into "Estimated monthly household income" — the coverage
    /// selection page's own income field, and the sole dependency of the review page's "Estimated
    /// annual total" calc (<c>calc-engine-plan.md</c>, Increment C) — and commits it on blur, the
    /// same way a respondent tabbing away from the field would.
    /// </summary>
    public static async Task FillEstimatedMonthlyIncomeAsync(IPage page, string amount)
    {
        var income = page.GetByLabel("Estimated monthly household income");
        await income.FillAsync(amount).ConfigureAwait(false);
        await income.BlurAsync().ConfigureAwait(false);
    }
}
