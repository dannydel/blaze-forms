using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Enforces the "Styles = CSS isolation only" rule from AGENTS.md's Blazor standards: every
/// collocated <c>.razor.css</c> file expresses color, spacing, radius, and font values through
/// <c>--bf-*</c> custom properties, never a hard-coded hex or <c>rgb()</c>/<c>rgba()</c> literal.
/// <c>wwwroot/blazeforms.css</c> is exempt — it is where the tokens themselves are declared with
/// real values (PRD §10, Phase 1) — this guard scans only the component-scoped stylesheets.
/// </summary>
public sealed class ComponentCssTokenGuardTests
{
    private static readonly Regex HexColorPattern = new(
        @"#[0-9A-Fa-f]{3,8}\b",
        RegexOptions.Compiled);

    private static readonly Regex RgbFunctionPattern = new(
        @"\brgba?\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void NoRazorCssFileContainsARawColorLiteral()
    {
        var violations = new List<string>();

        foreach (var path in FindRazorCssFiles())
        {
            var css = StripComments(File.ReadAllText(path));

            if (HexColorPattern.IsMatch(css) || RgbFunctionPattern.IsMatch(css))
            {
                violations.Add(path);
            }
        }

        Assert.True(
            violations.Count == 0,
            $"""
            The following .razor.css files declare a raw hex or rgb()/rgba() color literal
            instead of a --bf-* token reference:
            {string.Join(Environment.NewLine, violations)}
            """);
    }

    [Fact]
    public void AtLeastOneRazorCssFileWasFound()
    {
        // Guards the guard: if the glob ever stops matching anything (a folder move, a renamed
        // extension), the positive test above would pass vacuously and hide a real regression.
        Assert.NotEmpty(FindRazorCssFiles());
    }

    private static string StripComments(string css) => Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    private static string[] FindRazorCssFiles([CallerFilePath] string testFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFilePath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(testsDirectory, "..", ".."));
        var rendererSourceRoot = Path.Combine(repositoryRoot, "src", "BlazeForms.Renderer");

        return Directory.GetFiles(rendererSourceRoot, "*.razor.css", SearchOption.AllDirectories);
    }
}
