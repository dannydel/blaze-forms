using System.Globalization;
using System.Runtime.CompilerServices;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Computes WCAG contrast ratios for the shipped default theme's color tokens — the check
/// axe-core cannot perform, because axe only judges rendered elements, not a token contract a
/// host might restyle onto (docs/accessibility-statement-plan.md, Increment A). Parses the
/// literal <c>:root</c> values out of <c>wwwroot/blazeforms.css</c> via
/// <see cref="CssRootTokenParser"/>, then runs every pair the token contract
/// (docs/theming.md, "Contrast guarantees") promises through <see cref="WcagContrast.Ratio"/>.
/// </summary>
public sealed class ThemeContrastTests
{
    private const double TextContrastMinimum = 4.5;
    private const double NonTextContrastMinimum = 3.0;

    [Theory]
    [InlineData("--bf-color-text")]
    [InlineData("--bf-color-muted")]
    [InlineData("--bf-color-primary")]
    [InlineData("--bf-color-danger")]
    public void ForegroundTokenMeetsTextContrastAgainstBg(string foregroundToken)
    {
        var tokens = ParseRootTokens();

        var ratio = WcagContrast.Ratio(tokens[foregroundToken], tokens["--bf-color-bg"]);

        Assert.True(
            ratio >= TextContrastMinimum,
            $"{foregroundToken} against --bf-color-bg is {Format(ratio)}:1, below the {TextContrastMinimum}:1 WCAG 1.4.3 minimum.");
    }

    [Theory]
    [InlineData("--bf-color-text")]
    [InlineData("--bf-color-muted")]
    [InlineData("--bf-color-primary")]
    [InlineData("--bf-color-danger")]
    public void ForegroundTokenMeetsTextContrastAgainstSurface(string foregroundToken)
    {
        var tokens = ParseRootTokens();

        var ratio = WcagContrast.Ratio(tokens[foregroundToken], tokens["--bf-color-surface"]);

        Assert.True(
            ratio >= TextContrastMinimum,
            $"{foregroundToken} against --bf-color-surface is {Format(ratio)}:1, below the {TextContrastMinimum}:1 WCAG 1.4.3 minimum.");
    }

    [Theory]
    [InlineData("--bf-color-primary", "--bf-color-primary-contrast")]
    [InlineData("--bf-color-danger", "--bf-color-danger-contrast")]
    public void ContrastForegroundTokenMeetsTextContrastAgainstItsOwnBackground(string backgroundToken, string foregroundToken)
    {
        var tokens = ParseRootTokens();

        var ratio = WcagContrast.Ratio(tokens[foregroundToken], tokens[backgroundToken]);

        Assert.True(
            ratio >= TextContrastMinimum,
            $"{foregroundToken} against {backgroundToken} is {Format(ratio)}:1, below the {TextContrastMinimum}:1 WCAG 1.4.3 minimum.");
    }

    [Theory]
    [InlineData("--bf-color-border")]
    [InlineData("--bf-color-focus-ring")]
    public void NonTextTokenMeetsBoundaryContrastAgainstBg(string nonTextToken)
    {
        var tokens = ParseRootTokens();

        var ratio = WcagContrast.Ratio(tokens[nonTextToken], tokens["--bf-color-bg"]);

        Assert.True(
            ratio >= NonTextContrastMinimum,
            $"{nonTextToken} against --bf-color-bg is {Format(ratio)}:1, below the {NonTextContrastMinimum}:1 WCAG 1.4.11 minimum.");
    }

    [Theory]
    [InlineData("--bf-color-border")]
    [InlineData("--bf-color-focus-ring")]
    public void NonTextTokenMeetsBoundaryContrastAgainstSurface(string nonTextToken)
    {
        var tokens = ParseRootTokens();

        var ratio = WcagContrast.Ratio(tokens[nonTextToken], tokens["--bf-color-surface"]);

        Assert.True(
            ratio >= NonTextContrastMinimum,
            $"{nonTextToken} against --bf-color-surface is {Format(ratio)}:1, below the {NonTextContrastMinimum}:1 WCAG 1.4.11 minimum.");
    }

    /// <summary>
    /// Formats a ratio to three decimal places with an explicit invariant culture — two decimal
    /// places would round a real 2.996:1 failure to a misleadingly clean-looking "3.00:1",
    /// reading like a tooling bug instead of the near-miss it actually is.
    /// </summary>
    private static string Format(double ratio) => ratio.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>
    /// Only the tokens with a plain <c>#rrggbb</c> value are meaningful to
    /// <see cref="WcagContrast.Ratio"/> — every pair this class asserts on is one of those, so a
    /// caller asking for anything else (a <c>color-mix()</c> Designer token, say) is out of
    /// scope for this class and belongs in <c>DesignerContrastTests</c> instead.
    /// </summary>
    private static Dictionary<string, string> ParseRootTokens() =>
        CssRootTokenParser.ParseRootTokens(File.ReadAllText(BlazeFormsCssPath()));

    private static string BlazeFormsCssPath([CallerFilePath] string testFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFilePath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(testsDirectory, "..", ".."));

        return Path.Combine(repositoryRoot, "src", "BlazeForms.Renderer", "wwwroot", "blazeforms.css");
    }
}
