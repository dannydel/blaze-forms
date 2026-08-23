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

    private static string Format(double ratio) => ratio.ToString("F3", CultureInfo.InvariantCulture);

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
