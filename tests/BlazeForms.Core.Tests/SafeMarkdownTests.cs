using BlazeForms.Markdown;

namespace BlazeForms.Core.Tests;

/// <summary>
/// PRD §5.1 / AGENTS.md invariant #6: author Markdown renders only through the shared safe
/// pipeline — raw HTML disabled, link protocols allow-listed, images stripped.
/// </summary>
public sealed class SafeMarkdownTests
{
    [Fact]
    public void JavascriptLinkIsUnwrappedToItsTextWithNoAnchor()
    {
        var html = SafeMarkdown.ToHtml("[run me](javascript:alert('x'))").Value;

        Assert.Contains("run me", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<a", html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", html, StringComparison.Ordinal);
    }

    [Fact]
    public void DataLinkIsUnwrappedToItsTextWithNoAnchor()
    {
        var html = SafeMarkdown.ToHtml("[click](data:text/html,<b>hi</b>)").Value;

        Assert.Contains("click", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<a", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data:", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RawScriptIsRenderedAsEscapedTextNotLiveMarkup()
    {
        var html = SafeMarkdown.ToHtml("<script>alert('x')</script>").Value;

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RawImageWithHandlerIsRenderedAsEscapedTextNotLiveMarkup()
    {
        var html = SafeMarkdown.ToHtml("<img src=x onerror=alert(1)>").Value;

        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalHttpsLinkCarriesRelNoopenerNoreferrer()
    {
        var html = SafeMarkdown.ToHtml("[policy](https://example.gov/policy)").Value;

        Assert.Contains("<a", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.gov/policy\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener noreferrer\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void MailtoLinkIsAllowedAndCarriesNoRel()
    {
        var html = SafeMarkdown.ToHtml("[email us](mailto:help@example.gov)").Value;

        Assert.Contains("href=\"mailto:help@example.gov\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void EmphasisAndListsRenderAsSemanticElements()
    {
        var html = SafeMarkdown.ToHtml("This is *important*.\n\n- one\n- two").Value;

        Assert.Contains("<em>important</em>", html, StringComparison.Ordinal);
        Assert.Contains("<ul>", html, StringComparison.Ordinal);
        Assert.Contains("<li>one</li>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageSyntaxIsNotRenderedAsAnImage()
    {
        var html = SafeMarkdown.ToHtml("![a diagram](https://example.gov/diagram.png)").Value;

        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains("a diagram", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullEmptyOrWhitespaceRendersToEmpty(string? markdown)
    {
        Assert.Equal("", SafeMarkdown.ToHtml(markdown).Value);
    }

    [Fact]
    public void DefaultSafeHtmlValueIsEmptyAndToStringReturnsValue()
    {
        Assert.Equal("", default(SafeHtml).Value);
        Assert.Equal("<p>hi</p>", new SafeHtml("<p>hi</p>").ToString());
    }

    [Fact]
    public void ExtractLinksReturnsTextUrlAndExternalFlagAndExcludesImages()
    {
        const string markdown =
            "See the [policy page](https://example.gov/policy) or [email us](mailto:help@example.gov). "
            + "![a diagram](https://example.gov/diagram.png)";

        var links = SafeMarkdown.ExtractLinks(markdown);

        Assert.Equal(2, links.Count);

        Assert.Equal("policy page", links[0].Text);
        Assert.Equal("https://example.gov/policy", links[0].Url);
        Assert.True(links[0].IsExternal);

        Assert.Equal("email us", links[1].Text);
        Assert.Equal("mailto:help@example.gov", links[1].Url);
        Assert.False(links[1].IsExternal);
    }

    [Fact]
    public void ExtractLinksReturnsEmptyForBlankInput()
    {
        Assert.Empty(SafeMarkdown.ExtractLinks(null));
        Assert.Empty(SafeMarkdown.ExtractLinks("   "));
    }

    // A live disallowed href is the thing the pipeline must never emit, however the scheme is
    // dressed up. These assertions catch it in either quote style and case-insensitively.
    private static void AssertNoLiveDangerousHref(string html)
    {
        Assert.DoesNotContain("href=\"javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href='javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"data:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href='data:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[run me](JavaScript:doThing)")]
    [InlineData("[run me](JAVASCRIPT:doThing)")]
    [InlineData("[run me]( javascript:doThing)")]
    [InlineData("[run me](&#106;avascript:doThing)")]
    [InlineData("[run me](&#x4A;avascript:doThing)")]
    [InlineData("[run me](<java\tscript:doThing>)")]
    [InlineData("[run me](<\tjavascript:doThing>)")]
    public void ObfuscatedDisallowedSchemesAreUnwrappedToText(string markdown)
    {
        var html = SafeMarkdown.ToHtml(markdown).Value;

        AssertNoLiveDangerousHref(html);
        Assert.DoesNotContain("<a", html, StringComparison.Ordinal);
        Assert.Contains("run me", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RawAnchorWithJavascriptHrefIsEscapedNotLive()
    {
        var html = SafeMarkdown.ToHtml("<a href=\"javascript:alert(1)\">click</a>").Value;

        AssertNoLiveDangerousHref(html);
        Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);
        Assert.Contains("&lt;a", html, StringComparison.Ordinal);
    }

    [Fact]
    public void UppercaseHttpSchemeStillRendersAsAnExternalLinkWithRel()
    {
        var html = SafeMarkdown.ToHtml("[site](HTTP://example.gov)").Value;

        Assert.Contains("<a", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener noreferrer\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RelativeAndFragmentLinksWithNoSchemeStillRender()
    {
        var relative = SafeMarkdown.ToHtml("[policy](/policy/page)").Value;
        Assert.Contains("href=\"/policy/page\"", relative, StringComparison.Ordinal);

        var fragment = SafeMarkdown.ToHtml("[top](#section)").Value;
        Assert.Contains("href=\"#section\"", fragment, StringComparison.Ordinal);
    }
}
