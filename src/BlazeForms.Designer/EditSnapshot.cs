using BlazeForms.Definitions;

namespace BlazeForms.Designer;

/// <summary>
/// One entry on a <see cref="DesignerEditContext"/>'s undo or redo stack: the definition and
/// selection to restore, plus the localized description of the mutation that moved the draft away
/// from this state. Internal -- <see cref="DesignerEditContext.Undo"/> and
/// <see cref="DesignerEditContext.Redo"/> are the only supported way to act on one.
/// </summary>
internal sealed record EditSnapshot
{
    /// <summary>
    /// The definition to restore <see cref="DesignerEditContext.Draft"/>'s content to.
    /// </summary>
    public required FormDefinition Definition { get; init; }

    /// <summary>
    /// The selection to restore <see cref="DesignerEditContext.Selection"/> to, so undoing or
    /// redoing lands focus on the node the change actually affected rather than wherever the
    /// author happened to be looking at the time.
    /// </summary>
    public required DesignerSelection Selection { get; init; }

    /// <summary>
    /// The localized, plain-language description of the mutation this snapshot sits next to on
    /// the stack -- the same text that mutation announced, reused verbatim in "Undid: {0}" and
    /// "Redid: {0}".
    /// </summary>
    public required string Description { get; init; }
}
