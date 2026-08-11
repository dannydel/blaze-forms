using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Linting;

/// <summary>
/// Fills <c>CanvasNodeRow</c>'s own inline-lint slot with the node's current findings (PRD §8,
/// §4.1): one line per <see cref="LintResult"/>, each naming its severity in text (never color
/// alone) alongside a decorative, <c>aria-hidden</c> icon that also differs by severity, and the
/// finding's own plain-language message. Renders nothing at all when
/// <see cref="Findings"/> is empty, so <c>CanvasNodeRow.razor.css</c>'s own
/// <c>.bf-canvas-row__lint:empty</c> rule keeps collapsing the slot to nothing for a node with no
/// findings, the same as before this phase filled it in.
/// </summary>
public partial class InlineLintMarker : ComponentBase
{
    /// <summary>
    /// The findings <see cref="Canvas.DesignerCanvas"/> has already narrowed to this row's own
    /// node, in lint-rule order.
    /// </summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<LintResult> Findings { get; set; } = [];

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private static string SeverityModifier(LintResult finding) =>
        finding.Severity == LintSeverity.Blocking ? "blocking" : "advisory";

    private static string SeverityIcon(LintResult finding) => finding.Severity == LintSeverity.Blocking ? "⛔" : "⚠";

    private static string SeverityLabel(LintResult finding) => finding.Severity == LintSeverity.Blocking
        ? Localizer["LinterDockSeverityBlocking"].Value
        : Localizer["LinterDockSeverityAdvisory"].Value;

    private static string ItemKey(LintResult finding) => $"{finding.RuleId}|{finding.Message}";
}
