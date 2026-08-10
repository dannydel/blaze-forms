using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdigMarkdown = Markdig.Markdown;

namespace BlazeForms.Markdown;

/// <summary>
/// The one shared safe-Markdown pipeline (PRD §5.1). Author content in <c>help</c>,
/// <c>paragraph</c>, and <c>callout</c> renders through here so the renderer and the linter agree
/// on a single policy: CommonMark with raw HTML disabled, link protocols allow-listed to
/// <c>http</c>, <c>https</c>, and <c>mailto</c>, external links marked
/// <c>rel="noopener noreferrer"</c>, and images stripped. Definitions are untrusted input, so this
/// sanitization is a correctness requirement, not hardening (AGENTS.md invariant #6).
/// </summary>
/// <remarks>
/// Rendering is a parse → walk → render, not a one-shot conversion, because the mutation happens
/// on the syntax tree: a disallowed link is unwrapped to its literal text, an image is dropped to
/// its alt text, and an allowed external link gains its <c>rel</c> attribute — all before the tree
/// reaches the renderer.
/// </remarks>
public static class SafeMarkdown
{
    private const string ExternalLinkRel = "noopener noreferrer";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .Build();

    private static readonly string[] AllowedSchemes = ["http", "https", "mailto"];

    /// <summary>
    /// Renders author Markdown to sanitized HTML.
    /// </summary>
    /// <param name="markdown">
    /// The author's Markdown source. Null, empty, or whitespace renders to an empty result.
    /// </param>
    /// <returns>
    /// The sanitized HTML, wrapped in a <see cref="SafeHtml"/> so the invariant travels with the
    /// value.
    /// </returns>
    public static SafeHtml ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new SafeHtml("");
        }

        var document = MarkdigMarkdown.Parse(markdown, Pipeline);
        SanitizeLinks(document);

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();

        return new SafeHtml(writer.ToString());
    }

    /// <summary>
    /// Lists the links an author's Markdown contains, so the linter can inspect their text
    /// (A11Y-09, PRD §8). Images are excluded.
    /// </summary>
    /// <param name="markdown">
    /// The author's Markdown source. Null, empty, or whitespace yields no links.
    /// </param>
    /// <returns>
    /// The links, in document order, each carrying its concatenated literal text, its URL as
    /// authored, and whether that URL is external.
    /// </returns>
    public static IReadOnlyList<MarkdownLink> ExtractLinks(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }

        var document = MarkdigMarkdown.Parse(markdown, Pipeline);
        var links = new List<MarkdownLink>();

        foreach (var link in document.Descendants<LinkInline>().Where(link => !link.IsImage))
        {
            var url = link.Url ?? "";

            links.Add(new MarkdownLink
            {
                Text = ConcatenateLiteralText(link),
                Url = url,
                IsExternal = IsExternalHttp(url),
            });
        }

        return links;
    }

    private static void SanitizeLinks(MarkdownDocument document)
    {
        foreach (var link in document.Descendants<LinkInline>().ToArray())
        {
            if (link.IsImage)
            {
                // An image renders no answerable content; drop it to its alt text so no <img> is
                // emitted (PRD scopes author Markdown to emphasis, lists, and links).
                link.ReplaceBy(new LiteralInline(ConcatenateLiteralText(link)), copyChildren: false);
                continue;
            }

            var url = link.Url ?? "";

            if (DeclaresScheme(url, out var scheme) && !IsCleanAllowedScheme(scheme))
            {
                // javascript:, data:, vbscript:, an obfuscated scheme like "java\tscript", or a
                // bare ":path" — anything that declares a scheme that is not a clean
                // http/https/mailto is dropped to its literal text (keep the words, drop the
                // anchor).
                link.ReplaceBy(new LiteralInline(ConcatenateLiteralText(link)), copyChildren: false);
                continue;
            }

            if (IsExternalHttp(url))
            {
                link.GetAttributes().AddPropertyIfNotExist("rel", ExternalLinkRel);
            }
        }

        foreach (var autolink in document.Descendants<AutolinkInline>().ToArray())
        {
            var url = autolink.Url ?? "";

            if (DeclaresScheme(url, out var scheme) && !IsCleanAllowedScheme(scheme))
            {
                autolink.ReplaceBy(new LiteralInline(url), copyChildren: false);
                continue;
            }

            if (IsExternalHttp(url))
            {
                autolink.GetAttributes().AddPropertyIfNotExist("rel", ExternalLinkRel);
            }
        }
    }

    private static string ConcatenateLiteralText(ContainerInline container)
    {
        var builder = new StringBuilder();

        foreach (var literal in container.Descendants<LiteralInline>())
        {
            builder.Append(literal.Content.ToString());
        }

        return builder.ToString();
    }

    private static bool DeclaresScheme(string url, out string leadingSegment)
    {
        leadingSegment = "";

        // A URL declares a scheme when a colon appears before any path, query, or fragment. The
        // segment before that colon is judged as-is — a control character or percent sequence in
        // it never reads as a clean scheme, so an obfuscated "java\tscript:" cannot slip through as
        // "no scheme / relative". A colon that only appears after a '/', '?', or '#' is part of the
        // path (or absent), which leaves genuine relative and fragment links untouched.
        for (var index = 0; index < url.Length; index++)
        {
            var character = url[index];

            if (character == ':')
            {
                leadingSegment = url[..index];
                return true;
            }

            if (character is '/' or '?' or '#')
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsCleanAllowedScheme(string segment)
    {
        if (segment.Length == 0 || !char.IsAsciiLetter(segment[0]))
        {
            return false;
        }

        for (var index = 1; index < segment.Length; index++)
        {
            var character = segment[index];

            if (!(char.IsAsciiLetterOrDigit(character) || character is '+' or '.' or '-'))
            {
                return false;
            }
        }

        return AllowedSchemes.Contains(segment, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsExternalHttp(string url) =>
        DeclaresScheme(url, out var scheme)
        && IsCleanAllowedScheme(scheme)
        && (string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase));
}
