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
}
