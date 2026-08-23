using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Parses <c>--bf-*: value;</c> declarations out of a stylesheet's first <c>:root</c> block —
/// shared by <see cref="ThemeContrastTests"/>, the breakpoint literal-vs-token guards in
/// <c>ThemeCssTests</c>, and (via this project's reference from
/// <c>BlazeForms.Designer.Tests</c>) the Designer's own token and contrast tests. Comments are
/// stripped before the block is located, so a doc comment that happens to mention
/// <c>:root { ... }</c> or a token name in prose can never be mistaken for the real declaration
/// block.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BlazeForms.Designer.Tests calls this via a project reference to this assembly -- CA1515 has no way to know that cross-project use is intended, and InternalsVisibleTo would just trade one visibility escape hatch for another.")]
public static class CssRootTokenParser
{
    /// <summary>
    /// Every <c>--bf-*</c> token declared in <paramref name="css"/>'s first real <c>:root</c>
    /// block, keyed by token name, value as the literal right-hand side (untyped — a hex color,
    /// a <c>px</c>/<c>rem</c> length, or a <c>color-mix()</c> expression all come back as their
    /// own source text, trimmed).
    /// </summary>
    public static Dictionary<string, string> ParseRootTokens(string css)
    {
        var rootBlock = ExtractFirstRootBlock(StripComments(css));
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

        // ;|\z (not a bare trailing ;) so a legal semicolon-less final declaration -- allowed by
        // the CSS grammar immediately before the block's closing brace -- is still captured
        // instead of silently dropped.
        foreach (Match match in Regex.Matches(rootBlock, @"(--bf-[\w-]+)\s*:\s*([^;]+?)\s*(?:;|\z)"))
        {
            tokens[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        }

        return tokens;
    }

    private static string StripComments(string css) => Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    /// <summary>
    /// Anchored to a <c>:root</c> that starts its own line (allowing only leading whitespace)
    /// and is immediately followed by <c>{</c> — a real declaration block, never a substring
    /// match against prose that merely names <c>:root</c>.
    /// </summary>
    private static string ExtractFirstRootBlock(string strippedCss)
    {
        var match = Regex.Match(strippedCss, @"^[ \t]*:root[ \t]*\{", RegexOptions.Multiline);

        if (!match.Success)
        {
            throw new InvalidOperationException("No :root block found in the given stylesheet.");
        }

        var openBraceIndex = match.Index + match.Length - 1;
        var closeBraceIndex = strippedCss.IndexOf('}', openBraceIndex);

        if (closeBraceIndex < 0)
        {
            throw new InvalidOperationException("The :root block found has no closing brace -- the stylesheet is malformed.");
        }

        return strippedCss[(openBraceIndex + 1)..closeBraceIndex];
    }
}
