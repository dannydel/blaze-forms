using System.Runtime.CompilerServices;
using BlazeForms.Renderer.Tests;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Pins the Designer's own token contract (PRD §4.1, docs/theming.md "Designer tokens"):
/// every documented <c>--bf-*</c> custom property new to <c>blazeforms-designer.css</c> must
/// actually be declared there. Mirrors <c>BlazeForms.Renderer.Tests.ThemeCssTests</c>'s approach
/// — the renderer's stylesheet had this guard from the start; the designer's had none
/// (docs/accessibility-statement-plan.md, Increment A2).
/// </summary>
public sealed class DesignerThemeCssTests
{
    private static readonly string[] DocumentedTokens =
    [
        "--bf-palette-width",
        "--bf-properties-width",
        "--bf-pane-gap",
        "--bf-dock-height",
        "--bf-canvas-row-selected-bg",
        "--bf-publish-note-min-height",
        "--bf-breakpoint-dock-collapse",
    ];

    [Fact]
    public void DeclaresEveryDocumentedToken()
    {
        // Parsed via CssRootTokenParser (which strips comments first), not a bare substring
        // search on the raw file text -- a commented-out declaration would satisfy
        // Assert.Contains($"{token}:", ...) while declaring nothing real.
        var tokens = CssRootTokenParser.ParseRootTokens(File.ReadAllText(DesignerCssPath()));

        foreach (var token in DocumentedTokens)
        {
            Assert.True(tokens.ContainsKey(token), $"{token} is documented but not declared in blazeforms-designer.css's :root block.");
        }
    }

    /// <summary>
    /// <c>@media</c> can't reference a custom property, so <c>FormDesigner.razor.css</c>'s dock-
    /// collapse query carries its own literal copy of <c>--bf-breakpoint-dock-collapse</c>'s
    /// value — a comment says to keep the two in sync, but nothing enforced it before this test.
    /// Mirrors <c>ThemeCssTests.CollapseBreakpointMediaQueryLiteralMatchesItsOwnToken</c> for the
    /// renderer's own <c>--bf-breakpoint-collapse</c> pair.
    /// </summary>
    [Fact]
    public void DockCollapseBreakpointMediaQueryLiteralMatchesItsOwnToken()
    {
        var breakpoint = CssRootTokenParser.ParseRootTokens(File.ReadAllText(DesignerCssPath()))["--bf-breakpoint-dock-collapse"];
        var designerRazorCss = File.ReadAllText(FormDesignerRazorCssPath());

        Assert.Contains($"@media (max-width: {breakpoint})", designerRazorCss, StringComparison.Ordinal);
    }

    private static string DesignerCssPath([CallerFilePath] string testFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFilePath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(testsDirectory, "..", ".."));

        return Path.Combine(repositoryRoot, "src", "BlazeForms.Designer", "wwwroot", "blazeforms-designer.css");
    }

    private static string FormDesignerRazorCssPath([CallerFilePath] string testFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFilePath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(testsDirectory, "..", ".."));

        return Path.Combine(repositoryRoot, "src", "BlazeForms.Designer", "FormDesigner.razor.css");
    }
}
