using BlazeForms.Definitions;
using BlazeForms.Fields;
using Bunit;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers the four static-content components — <see cref="HeadingBlock"/>,
/// <see cref="ParagraphBlock"/>, <see cref="CalloutBlock"/>, <see cref="DividerBlock"/> — none of
/// which carry an answer.
/// </summary>
public sealed class StaticBlocksTests : BunitContext
{
    [Theory]
    [InlineData(2, "h2")]
    [InlineData(3, "h3")]
    [InlineData(4, "h4")]
    [InlineData(null, "h2")]
    public void HeadingBlockEmitsTheElementMatchingItsLevel(int? level, string expectedTag)
    {
        var node = TestNodes.Create(NodeType.Heading, label: "Section title", level: level);
        var cut = Render<HeadingBlock>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        var heading = cut.Find(expectedTag);
        Assert.Equal("Section title", heading.TextContent);
        Assert.Equal("f1", heading.GetAttribute("id"));
    }

    [Fact]
    public void HeadingBlockRendersLabelAsPlainTextEvenWhenItLooksLikeMarkdown()
    {
        var node = TestNodes.Create(NodeType.Heading, label: "**Not bold** <script>alert(1)</script>");
        var cut = Render<HeadingBlock>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        // A label is never a Markdown/markup path (PRD §5.1) -- the literal asterisks and tag
        // text must appear verbatim, not be parsed as emphasis or, worse, raw HTML.
        Assert.Contains("**Not bold**", cut.Find("h2").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("script"));
    }

    [Fact]
    public void ParagraphBlockRendersSanitizedMarkdownContent()
    {
        var node = TestNodes.Create(NodeType.Paragraph, content: "Some *emphasis* and a [link](https://example.gov).");
        var cut = Render<ParagraphBlock>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        Assert.NotEmpty(cut.FindAll("em"));
        var link = cut.Find("a");
        Assert.Equal("https://example.gov", link.GetAttribute("href"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
    }

    [Fact]
    public void ParagraphBlockStripsAnInjectedScriptFromContent()
    {
        var node = TestNodes.Create(NodeType.Paragraph, content: "Before <script>alert(1)</script> after.");
        var cut = Render<ParagraphBlock>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        Assert.Empty(cut.FindAll("script"));
        Assert.DoesNotContain("<script", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CalloutBlockRendersAsANoteWithSanitizedContentAndNeutralizesAJavascriptLink()
    {
        var node = TestNodes.Create(NodeType.Callout, content: "Read this [link](javascript:alert(1)) carefully.");
        var cut = Render<CalloutBlock>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        var callout = cut.Find("[role='note']");
        Assert.Equal("f1", callout.GetAttribute("id"));
        Assert.Empty(cut.FindAll("a"));
        Assert.Contains("link", callout.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void DividerBlockRendersAnHrWithTheFieldIdAsAnAnchor()
    {
        var node = TestNodes.Create(NodeType.Divider, label: null);
        var cut = Render<DividerBlock>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        Assert.Equal("f1", cut.Find("hr").GetAttribute("id"));
    }
}
