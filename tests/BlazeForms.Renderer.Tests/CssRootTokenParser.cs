using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Parses <c>--bf-*: value;</c> declarations out of a stylesheet's first block matching a given
/// selector — shared by <see cref="ThemeContrastTests"/>, the breakpoint literal-vs-token guards
/// in <c>ThemeCssTests</c>, and (via this project's reference from
/// <c>BlazeForms.Designer.Tests</c>) the Designer's own token and contrast tests. Comments are
/// stripped before a block is located, so a doc comment that happens to mention
/// <c>:root { ... }</c> or a token name in prose can never be mistaken for the real declaration
/// block. Increment C (docs/accessibility-statement-plan.md) generalized this from a
/// <c>:root</c>-only parser to an attribute-selector-aware one, so the opt-in
/// <c>[data-bf-theme="high-contrast"]</c> token block gets the same parsing this class already
/// gives <c>:root</c> — one parser, three selector shapes, rather than a second copy-pasted class.
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
    /// own source text, trimmed). Equivalent to <c>ParseTokensForSelector(css, ":root")</c>.
    /// </summary>
    public static Dictionary<string, string> ParseRootTokens(string css) => ParseTokensForSelector(css, ":root");

    /// <summary>
    /// Every <c>--bf-*</c> token declared in <paramref name="css"/>'s first block whose selector
    /// text is exactly <paramref name="selector"/> (e.g. <c>:root</c> or
    /// <c>[data-bf-theme="high-contrast"]</c>) — the same value grammar <see cref="ParseRootTokens"/>
    /// parses, just located by an arbitrary selector rather than only <c>:root</c>.
    /// </summary>
    public static Dictionary<string, string> ParseTokensForSelector(string css, string selector) =>
        ParseDeclarations(ExtractFirstBlock(StripComments(css), selector));

    /// <summary>
    /// Every <c>--bf-*</c> token declared in the first block whose selector text is exactly
    /// <paramref name="selector"/>, searched only within the brace-balanced BODY of the first
    /// <c>@media</c> rule whose prelude contains <paramref name="mediaFeature"/> (e.g.
    /// <c>"prefers-contrast: more"</c>) — for a token block nested inside a media query, where a
    /// plain selector search would find the wrong block of the same selector text instead. The
    /// search is bounded to that rule's own closing <c>}</c> (found by counting nested braces, not
    /// by the first <c>}</c> encountered) so that if the intended nested block were ever removed,
    /// this throws instead of silently matching a same-named selector elsewhere in the stylesheet
    /// (e.g. a LATER, unrelated <c>@media</c> rule that also declares a <c>:root</c> block).
    /// </summary>
    public static Dictionary<string, string> ParseTokensForSelectorWithinMediaQuery(string css, string mediaFeature, string selector)
    {
        var stripped = StripComments(css);
        var mediaIndex = stripped.IndexOf(mediaFeature, StringComparison.Ordinal);

        if (mediaIndex < 0)
        {
            throw new InvalidOperationException($"No @media rule containing '{mediaFeature}' was found in the given stylesheet.");
        }

        var openBraceIndex = stripped.IndexOf('{', mediaIndex);

        if (openBraceIndex < 0)
        {
            throw new InvalidOperationException($"The @media rule containing '{mediaFeature}' has no opening brace -- the stylesheet is malformed.");
        }

        var body = stripped[(openBraceIndex + 1)..ExtractBalancedCloseBraceIndex(stripped, openBraceIndex)];

        return ParseDeclarations(ExtractFirstBlock(body, selector));
    }

    /// <summary>
    /// The index of the <c>}</c> that balances the <c>{</c> at <paramref name="openBraceIndex"/>,
    /// counting nested braces rather than trusting the first <c>}</c> encountered — an
    /// <c>@media</c> rule's own body contains at least one nested selector block, each with its
    /// own closing brace that is NOT the media rule's own.
    /// </summary>
    private static int ExtractBalancedCloseBraceIndex(string css, int openBraceIndex)
    {
        var depth = 1;

        for (var i = openBraceIndex + 1; i < css.Length; i++)
        {
            if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return i;
                }
            }
        }

        throw new InvalidOperationException("An @media rule's opening brace has no balancing closing brace -- the stylesheet is malformed.");
    }

    private static Dictionary<string, string> ParseDeclarations(string block)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

        // ;|\z (not a bare trailing ;) so a legal semicolon-less final declaration -- allowed by
        // the CSS grammar immediately before the block's closing brace -- is still captured
        // instead of silently dropped.
        foreach (Match match in Regex.Matches(block, @"(--bf-[\w-]+)\s*:\s*([^;]+?)\s*(?:;|\z)"))
        {
            tokens[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        }

        return tokens;
    }

    private static string StripComments(string css) => Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    /// <summary>
    /// Anchored to a <paramref name="selector"/> that starts its own line (allowing only leading
    /// whitespace) and is immediately followed by <c>{</c> — a real declaration block, never a
    /// substring match against prose that merely names the selector. <paramref name="selector"/>
    /// is escaped before becoming part of the regex, so a selector containing regex metacharacters
    /// (every attribute selector does, via its own brackets and quotes) is matched literally.
    /// </summary>
    private static string ExtractFirstBlock(string strippedCss, string selector)
    {
        var match = Regex.Match(strippedCss, $@"^[ \t]*{Regex.Escape(selector)}[ \t]*\{{", RegexOptions.Multiline);

        if (!match.Success)
        {
            throw new InvalidOperationException($"No '{selector}' block found in the given stylesheet.");
        }

        var openBraceIndex = match.Index + match.Length - 1;
        var closeBraceIndex = strippedCss.IndexOf('}', openBraceIndex);

        if (closeBraceIndex < 0)
        {
            throw new InvalidOperationException($"The '{selector}' block found has no closing brace -- the stylesheet is malformed.");
        }

        return strippedCss[(openBraceIndex + 1)..closeBraceIndex];
    }
}
