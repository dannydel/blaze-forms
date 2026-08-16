using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Fields;

/// <summary>
/// A read-only computed value (PRD §5, <see cref="Definitions.NodeType.Calc"/>).
/// <see cref="FormRenderer.RecomputeCalculations"/> writes the calc node's live result into the
/// renderer's answer store on every relevant change; the renderer formats it and hands it in
/// through <see cref="FormFieldBase.Value"/> as a <see cref="string"/>, which this component
/// shows as read-only text and never treats as an editable answer — it never invokes
/// <see cref="FormFieldBase.ValueChanged"/>, is never required, and is excluded from validation
/// (PRD §5, decision log D-E). <see cref="FormFieldBase.Value"/> being <see langword="null"/> —
/// a calc node with no <see cref="Definitions.FormNode.Calculation"/> at all, or one that has not
/// yet resolved to a value — falls back to <see cref="Definitions.FormNode.Placeholder"/>.
/// </summary>
/// <remarks>
/// <b>Accessibility (Increment C's "announce on commit" refinement).</b> The control is a
/// semantic <c>&lt;output&gt;</c> (decision log D-E #4), not an <c>&lt;input&gt;</c>: it carries no
/// editable-control semantics for assistive technology to begin with, so there is nothing to mark
/// <c>readonly</c> or <c>disabled</c>, and it sits out of the tab order the same way any
/// non-form-control text does. <c>&lt;output&gt;</c>'s implicit ARIA role is <c>status</c>, a
/// polite live region — announcing this element's own text on every recompute would mean
/// announcing on every keystroke for a number/currency dependency (they recompute on
/// <c>oninput</c>, PRD §4.2), which is disruptive rather than helpful. This element carries an
/// explicit <c>aria-live="off"</c> to suppress that: its visual text still updates silently on
/// every keystroke — the "value changed" feedback a sighted respondent gets for free by watching
/// the rendered text update — but a screen reader is told about the new value only once, when
/// <c>FormRenderer</c>'s own visually-hidden, <c>aria-live="polite"</c> calc-announcer region
/// refreshes on the dependency's own commit (blur), not on every intermediate keystroke. The
/// <c>&lt;label for&gt;</c> is still programmatically associated with this element via
/// <see cref="FormFieldBase.FieldId"/>, so its purpose is announced the same way every other
/// field's label is, and its current value stays perceivable on demand (a screen reader can
/// still navigate to and read it) even between announcements.
/// </remarks>
public partial class CalcField : FormFieldBase
{
    private string? _renderedDisplayText;

    /// <inheritdoc />
    protected override void CaptureValueSnapshot() => _renderedDisplayText = DisplayText;

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var changed = HaveSharedParametersChanged() || _renderedDisplayText != DisplayText;
        _renderedDisplayText = DisplayText;
        return changed;
    }

    private string DisplayText => (Value as string) ?? Node.Placeholder ?? "";

    private bool HasHelp => !string.IsNullOrWhiteSpace(Node.Help);

    private MarkupString HelpMarkup => new(SafeMarkdown.ToHtml(Node.Help).Value);
}
