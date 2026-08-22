using System.ComponentModel;
using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Fields;

/// <summary>
/// Static prose (PRD §5, <see cref="Definitions.NodeType.Paragraph"/>). Carries no answer, so it
/// ignores every value/validation parameter on <see cref="FormFieldBase"/>.
/// <see cref="Definitions.FormNode.Content"/> is Markdown-enabled (PRD §5.1) and renders only
/// through <see cref="SafeMarkdown.ToHtml"/> — the sole sanctioned raw-markup path (AGENTS.md
/// invariant #6).
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class ParagraphBlock : FormFieldBase
{
    private string? _renderedContent;

    /// <inheritdoc />
    protected override void CaptureValueSnapshot() => _renderedContent = Node.Content;

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var changed = HaveSharedParametersChanged() || _renderedContent != Node.Content;
        _renderedContent = Node.Content;
        return changed;
    }

    private MarkupString ContentMarkup => new(SafeMarkdown.ToHtml(Node.Content).Value);
}
