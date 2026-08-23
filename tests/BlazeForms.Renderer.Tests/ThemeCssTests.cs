using System.Runtime.CompilerServices;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Pins the shipped default theme's token contract (PRD §10, docs/theming.md): every
/// documented <c>--bf-*</c> custom property must actually be declared in
/// <c>wwwroot/blazeforms.css</c>, or a host following the theming doc would restyle a property
/// that does nothing.
/// </summary>
public sealed class ThemeCssTests
{
    /// <summary>
    /// The token registry documented in docs/theming.md. Kept as a literal golden list rather
    /// than parsed out of the doc, so a docs/CSS drift shows up as a failing assertion instead of
    /// silently agreeing with itself.
    /// </summary>
    private static readonly string[] DocumentedTokens =
    [
        "--bf-color-bg",
        "--bf-color-surface",
        "--bf-color-text",
        "--bf-color-muted",
        "--bf-color-border",
        "--bf-color-primary",
        "--bf-color-primary-contrast",
        "--bf-color-danger",
        "--bf-color-danger-contrast",
        "--bf-color-focus-ring",
        "--bf-font-sans",
        "--bf-font-size-sm",
        "--bf-font-size-base",
        "--bf-font-size-lg",
        "--bf-line-height",
        "--bf-space-1",
        "--bf-space-2",
        "--bf-space-3",
        "--bf-space-4",
        "--bf-space-5",
        "--bf-space-6",
        "--bf-radius-sm",
        "--bf-radius-md",
        "--bf-border-width",
        "--bf-focus-ring-width",
        "--bf-focus-ring-offset",
        "--bf-touch-target",
        "--bf-motion-duration",
        "--bf-motion-ease",
        "--bf-breakpoint-collapse",
    ];

    [Fact]
    public void DeclaresEveryDocumentedToken()
    {
        var css = File.ReadAllText(BlazeFormsCssPath());

        foreach (var token in DocumentedTokens)
        {
            Assert.Contains($"{token}:", css, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ZerosMotionUnderReducedMotionPreference()
    {
        var css = File.ReadAllText(BlazeFormsCssPath());

        Assert.Contains("prefers-reduced-motion: reduce", css, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaresAVisibleFocusRing()
    {
        var css = File.ReadAllText(BlazeFormsCssPath());

        Assert.Contains("focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("outline", css, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>@media</c> can't reference a custom property, so the <c>.bf-row</c> collapse query
    /// (blazeforms.css, just below this token's declaration) carries its own literal copy of
    /// <c>--bf-breakpoint-collapse</c>'s value — a comment says to keep the two in sync, but
    /// nothing enforced it before this test. A token bumped without its media query (or vice
    /// versa) now fails loudly instead of shipping a green build with a stale breakpoint.
    /// </summary>
    [Fact]
    public void CollapseBreakpointMediaQueryLiteralMatchesItsOwnToken()
    {
        var css = File.ReadAllText(BlazeFormsCssPath());
        var breakpoint = CssRootTokenParser.ParseRootTokens(css)["--bf-breakpoint-collapse"];

        Assert.Contains($"@media (max-width: {breakpoint})", css, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>.bf-visually-hidden</c> must live in this GLOBAL stylesheet, not a CSS-isolated
    /// <c>.razor.css</c> (docs/accessibility-statement-plan.md, Increment B3; resolved decision
    /// 7): a host applying the class to markup of its own -- an <c>&lt;h1&gt;</c>
    /// <c>&lt;FocusOnNavigate Selector="h1"&gt;</c> needs, say -- needs the rule to exist
    /// unscoped here, not compiled to <c>.bf-visually-hidden[b-xxxxxxx]</c> inside
    /// <c>FormRenderer.razor.css</c> where only markup <c>FormRenderer</c> itself renders can
    /// ever match it. Asserts the actual clip-based properties, not just that the selector
    /// appears (an empty <c>.bf-visually-hidden {}</c> rule would satisfy a bare
    /// <c>Contains(".bf-visually-hidden {")</c> check without hiding anything), and asserts the
    /// selector is ABSENT from the isolated file so "declared exactly once, globally" is an
    /// enforced invariant rather than something hand-verified at review time.
    /// </summary>
    [Fact]
    public void DeclaresTheVisuallyHiddenClassGloballyAndOnlyGlobally()
    {
        var globalCss = File.ReadAllText(BlazeFormsCssPath());
        var isolatedCss = File.ReadAllText(FormRendererIsolatedCssPath());

        var ruleStart = globalCss.IndexOf(".bf-visually-hidden {", StringComparison.Ordinal);
        Assert.True(ruleStart >= 0, "blazeforms.css does not declare .bf-visually-hidden.");

        var ruleEnd = globalCss.IndexOf('}', ruleStart);
        Assert.True(ruleEnd > ruleStart, ".bf-visually-hidden's own rule block in blazeforms.css never closes.");
        var rule = globalCss[ruleStart..ruleEnd];

        Assert.Contains("position: absolute", rule, StringComparison.Ordinal);
        Assert.Contains("clip:", rule, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden", rule, StringComparison.Ordinal);

        // Checks for the RULE (selector immediately followed by its declaration block), not a
        // bare substring match -- FormRenderer.razor.css's own remaining comment about where
        // .bf-visually-hidden moved to necessarily still names the class by text.
        Assert.DoesNotContain(".bf-visually-hidden {", isolatedCss, StringComparison.Ordinal);
    }

    /// <summary>
    /// Locates the shipped stylesheet relative to this test file's own path, using
    /// <see cref="CallerFilePathAttribute"/> — robust regardless of the test runner's working
    /// directory or output folder, unlike a path derived from <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    private static string BlazeFormsCssPath([CallerFilePath] string testFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFilePath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(testsDirectory, "..", ".."));

        return Path.Combine(repositoryRoot, "src", "BlazeForms.Renderer", "wwwroot", "blazeforms.css");
    }

    /// <summary>
    /// <c>FormRenderer</c>'s own CSS-isolated stylesheet — the file <c>.bf-visually-hidden</c>
    /// used to live in before Increment B3 moved it into the global sheet above.
    /// </summary>
    private static string FormRendererIsolatedCssPath([CallerFilePath] string testFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFilePath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(testsDirectory, "..", ".."));

        return Path.Combine(repositoryRoot, "src", "BlazeForms.Renderer", "FormRenderer.razor.css");
    }
}
