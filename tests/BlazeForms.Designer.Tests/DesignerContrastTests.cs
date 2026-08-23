using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BlazeForms.Renderer.Tests;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Computes WCAG contrast for the Designer's own color token —
/// <c>--bf-canvas-row-selected-bg</c>, the sighted equivalent of the roving-focus canvas
/// selection (docs/accessibility-statement-plan.md resolved decision 1). Reuses
/// <see cref="WcagContrast"/> and <see cref="CssRootTokenParser"/> from
/// <c>BlazeForms.Renderer.Tests</c> (via this project's reference to it) rather than duplicating
/// the luminance math a second time — <c>ThemeContrastTests</c> covers every renderer token pair;
/// this class is its Designer-side counterpart.
/// </summary>
public sealed class DesignerContrastTests
{
    private const double NonTextContrastMinimum = 3.0;
    private const double TextContrastMinimum = 4.5;

    [Fact]
    public void CanvasRowSelectedBackgroundMeetsBoundaryContrastAgainstBorder()
    {
        var selectedBackground = ResolveSelectedRowBackground();
        var border = RendererTokens()["--bf-color-border"];

        var ratio = WcagContrast.Ratio(selectedBackground, border);

        Assert.True(
            ratio >= NonTextContrastMinimum,
            $"--bf-canvas-row-selected-bg ({selectedBackground}) against --bf-color-border is {Format(ratio)}:1, below the {NonTextContrastMinimum}:1 WCAG 1.4.11 minimum.");
    }

    [Theory]
    [InlineData("--bf-color-text")]
    [InlineData("--bf-color-muted")]
    public void CanvasRowSelectedBackgroundMeetsTextContrastForContentRenderedOnIt(string textToken)
    {
        var selectedBackground = ResolveSelectedRowBackground();
        var foreground = RendererTokens()[textToken];

        var ratio = WcagContrast.Ratio(foreground, selectedBackground);

        Assert.True(
            ratio >= TextContrastMinimum,
            $"{textToken} against --bf-canvas-row-selected-bg ({selectedBackground}) is {Format(ratio)}:1, below the {TextContrastMinimum}:1 WCAG 1.4.3 minimum.");
    }

    [Fact]
    public void HighContrastThemeCanvasRowSelectedBackgroundMeetsBoundaryContrastAgainstBorder()
    {
        var selectedBackground = HighContrastSelectedRowBackground();
        var border = HighContrastRendererTokens()["--bf-color-border"];

        var ratio = WcagContrast.Ratio(selectedBackground, border);

        Assert.True(
            ratio >= NonTextContrastMinimum,
            $"[data-bf-theme=\"high-contrast\"] --bf-canvas-row-selected-bg ({selectedBackground}) against --bf-color-border is {Format(ratio)}:1, below the {NonTextContrastMinimum}:1 WCAG 1.4.11 minimum.");
    }

    [Fact]
    public void HighContrastThemeCanvasRowSelectedBackgroundMeetsTextContrastForPrimaryText()
    {
        var selectedBackground = HighContrastSelectedRowBackground();
        var text = HighContrastRendererTokens()["--bf-color-text"];

        var ratio = WcagContrast.Ratio(text, selectedBackground);

        Assert.True(
            ratio >= TextContrastMinimum,
            $"[data-bf-theme=\"high-contrast\"] --bf-color-text against --bf-canvas-row-selected-bg ({selectedBackground}) is {Format(ratio)}:1, below the {TextContrastMinimum}:1 WCAG 1.4.3 minimum.");
    }

    /// <summary>
    /// A DISCLOSED, NOT SOLVED, token-contract gap (docs/accessibility-statement-plan.md
    /// Increment C review, S1; docs/theming.md's "High contrast" section): the high-contrast
    /// theme's own pure black/white palette leaves no single background color that clears BOTH
    /// this pairing's 3:1 boundary floor against a pure white page background AND 4.5:1 text
    /// contrast for <c>--bf-color-muted</c> specifically (its very dark, ~0.05 relative-luminance
    /// value forces the background half of that trade well above the boundary ceiling — see
    /// <c>blazeforms-designer.css</c>'s own remarks on the arithmetic). This test pins the ACTUAL
    /// ratio as a known limitation, not a silent one: it must stay firmly below the text floor
    /// (so a future edit doesn't accidentally make the situation worse by fooling itself into
    /// thinking it passes) and firmly at or above the boundary floor (so it never regresses to
    /// something even less legible without this test failing loudly). If either bound is ever
    /// crossed, update this test's own assertions AND docs/theming.md's disclosure together.
    /// </summary>
    [Fact]
    public void HighContrastThemeMutedTextOnSelectedRowDoesNotClearTheTextFloor()
    {
        var selectedBackground = HighContrastSelectedRowBackground();
        var muted = HighContrastRendererTokens()["--bf-color-muted"];

        var ratio = WcagContrast.Ratio(muted, selectedBackground);

        Assert.True(
            ratio >= NonTextContrastMinimum,
            $"[data-bf-theme=\"high-contrast\"] --bf-color-muted against --bf-canvas-row-selected-bg ({selectedBackground}) regressed below even the {NonTextContrastMinimum}:1 boundary floor, at {Format(ratio)}:1.");
        Assert.True(
            ratio < TextContrastMinimum,
            $"""
            [data-bf-theme="high-contrast"] --bf-color-muted against --bf-canvas-row-selected-bg
            ({selectedBackground}) now measures {Format(ratio)}:1, which clears the
            {TextContrastMinimum}:1 text floor -- update docs/theming.md's "High contrast" section
            to remove the disclosed muted-text limitation, since it no longer applies.
            """);
    }

    private static string Format(double ratio) => ratio.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>
    /// The opt-in high-contrast theme's own literal <c>--bf-canvas-row-selected-bg</c> override —
    /// a plain <c>#rrggbb</c>, not a <c>color-mix()</c> formula like the default theme's
    /// (<see cref="ResolveSelectedRowBackground"/>), since the whole point of this override is
    /// that the derived formula cannot clear this pairing's floor under the high-contrast
    /// palette — see <c>blazeforms-designer.css</c>'s own remarks.
    /// </summary>
    private static string HighContrastSelectedRowBackground() =>
        CssRootTokenParser.ParseTokensForSelector(File.ReadAllText(DesignerCssPath()), "[data-bf-theme=\"high-contrast\"]")["--bf-canvas-row-selected-bg"];

    /// <summary>
    /// The renderer's high-contrast theme tokens — the ones the Designer's own high-contrast
    /// override above measures itself against, mirroring <see cref="RendererTokens"/>'s default-
    /// theme counterpart.
    /// </summary>
    private static Dictionary<string, string> HighContrastRendererTokens() =>
        CssRootTokenParser.ParseTokensForSelector(File.ReadAllText(RendererCssPath()), "[data-bf-theme=\"high-contrast\"]");

    /// <summary>
    /// Resolves <c>--bf-canvas-row-selected-bg</c>'s
    /// <c>color-mix(in srgb, var(--bf-color-primary) 12%, var(--bf-color-bg))</c> declaration to
    /// its own <c>#rrggbb</c> result by parsing the mix arithmetic and looking up both referenced
    /// tokens in the renderer's own stylesheet — this pins the actual rendered color (<c>#e2ebf9</c>
    /// today) rather than the unevaluated formula, so a change to either input token or the mix
    /// percentage is what this test actually protects against, not a string comparison against
    /// the formula's own source text.
    /// </summary>
    private static string ResolveSelectedRowBackground()
    {
        var designerTokens = CssRootTokenParser.ParseRootTokens(File.ReadAllText(DesignerCssPath()));
        var declaration = designerTokens["--bf-canvas-row-selected-bg"];

        var match = Regex.Match(
            declaration,
            @"color-mix\(in srgb,\s*var\((--bf-[\w-]+)\)\s*(\d+)%,\s*var\((--bf-[\w-]+)\)\)");

        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"--bf-canvas-row-selected-bg's declaration ('{declaration}') is no longer a color-mix() this resolver understands -- update it alongside the CSS change.");
        }

        var mixToken = match.Groups[1].Value;
        var mixPercent = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var baseToken = match.Groups[3].Value;

        var rendererTokens = RendererTokens();
        return MixHex(rendererTokens[mixToken], rendererTokens[baseToken], mixPercent);
    }

    private static string MixHex(string mixHex, string baseHex, int mixPercent)
    {
        var mix = ParseHex(mixHex);
        var baseColor = ParseHex(baseHex);
        var fraction = mixPercent / 100.0;

        var r = MixChannel(mix.R, baseColor.R, fraction);
        var g = MixChannel(mix.G, baseColor.G, fraction);
        var b = MixChannel(mix.B, baseColor.B, fraction);

        return $"#{r:x2}{g:x2}{b:x2}";
    }

    private static int MixChannel(int mixChannel, int baseChannel, double fraction) =>
        (int)Math.Round((mixChannel * fraction) + (baseChannel * (1 - fraction)), MidpointRounding.AwayFromZero);

    private static (int R, int G, int B) ParseHex(string hex)
    {
        var value = hex.TrimStart('#');

        return (
            Convert.ToInt32(value[..2], 16),
            Convert.ToInt32(value[2..4], 16),
            Convert.ToInt32(value[4..6], 16));
    }

    private static Dictionary<string, string> RendererTokens() =>
        CssRootTokenParser.ParseRootTokens(File.ReadAllText(RendererCssPath()));

    private static string DesignerCssPath([CallerFilePath] string testFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFilePath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(testsDirectory, "..", ".."));

        return Path.Combine(repositoryRoot, "src", "BlazeForms.Designer", "wwwroot", "blazeforms-designer.css");
    }

    private static string RendererCssPath([CallerFilePath] string testFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFilePath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(testsDirectory, "..", ".."));

        return Path.Combine(repositoryRoot, "src", "BlazeForms.Renderer", "wwwroot", "blazeforms.css");
    }
}
