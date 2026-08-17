namespace BlazeForms.Designer;

/// <summary>
/// What is currently selected on the canvas -- a page, a section, or a node -- plus why focus
/// landed there. Later phases read this to place real DOM focus after a mutation; this phase
/// only computes it (PRD §4.1, §11).
/// </summary>
/// <remarks>
/// The three identifier properties nest: a node selection always carries the section and page it
/// lives in, and a section selection always carries its page, so a consumer never has to walk the
/// draft back up the tree just to know which page is showing. <see cref="None"/> and the three
/// <c>For*</c> factories are the only supported ways to build one -- keeping every combination of
/// "which level is selected" to exactly the shapes the designer actually produces.
/// </remarks>
public sealed record DesignerSelection
{
    /// <summary>
    /// The selected node's page, or the selected section's or page's own page identifier.
    /// <see langword="null"/> only for <see cref="None"/>.
    /// </summary>
    public string? PageId { get; init; }

    /// <summary>
    /// The selected node's section, or the selected section's own identifier.
    /// <see langword="null"/> when nothing below the page level is selected.
    /// </summary>
    public string? SectionId { get; init; }

    /// <summary>
    /// The selected node's identifier. <see langword="null"/> when a section or a page, rather
    /// than a node, is selected.
    /// </summary>
    public string? NodeId { get; init; }

    /// <summary>
    /// The identifier of the repeating group the canvas is currently drilled into
    /// (repeating-groups-plan.md, Increment C's canvas drill-in scope) --
    /// <see langword="null"/> at the top level. When set alongside <see cref="NodeId"/>, that node
    /// is one of the group's own <c>Children</c>; when set with <see cref="NodeId"/>
    /// <see langword="null"/>, the scope is drilled in but has no child to select yet (an empty
    /// group), and a canvas reads that combination as "focus the scope's own heading". Carried on
    /// every <see cref="EditSnapshot"/> the same as every other property here, so undoing or
    /// redoing into a mutation made while scoped restores the scoped view, not just the
    /// definition (D-4's "the selection/scope snapshot must restore the right view").
    /// </summary>
    public string? GroupId { get; init; }

    /// <summary>
    /// Why focus landed here -- the signal a later phase's canvas uses to decide whether (and
    /// how) to move real DOM focus.
    /// </summary>
    public DesignerFocusIntent Intent { get; init; } = DesignerFocusIntent.None;

    /// <summary>
    /// Nothing selected -- the starting selection before any mutation has happened.
    /// </summary>
    public static DesignerSelection None { get; } = new();

    /// <summary>
    /// Selects a node.
    /// </summary>
    /// <param name="nodeId">
    /// The node's identifier.
    /// </param>
    /// <param name="pageId">
    /// The identifier of the page the node's section belongs to.
    /// </param>
    /// <param name="sectionId">
    /// The identifier of the section the node belongs to.
    /// </param>
    /// <param name="intent">
    /// Why focus is landing on this node.
    /// </param>
    /// <returns>
    /// A selection anchored to the node.
    /// </returns>
    public static DesignerSelection ForNode(string nodeId, string pageId, string sectionId, DesignerFocusIntent intent) =>
        new() { NodeId = nodeId, SectionId = sectionId, PageId = pageId, Intent = intent };

    /// <summary>
    /// Selects a section -- the anchor a delete falls back to when the deleted node was the
    /// section's only one, and the anchor a fresh section starts with.
    /// </summary>
    /// <param name="pageId">
    /// The identifier of the page the section belongs to.
    /// </param>
    /// <param name="sectionId">
    /// The section's identifier.
    /// </param>
    /// <param name="intent">
    /// Why focus is landing on this section.
    /// </param>
    /// <returns>
    /// A selection anchored to the section, with no node selected.
    /// </returns>
    public static DesignerSelection ForSection(string pageId, string sectionId, DesignerFocusIntent intent) =>
        new() { SectionId = sectionId, PageId = pageId, Intent = intent };

    /// <summary>
    /// Selects a page -- the anchor a fresh page starts with.
    /// </summary>
    /// <param name="pageId">
    /// The page's identifier.
    /// </param>
    /// <param name="intent">
    /// Why focus is landing on this page.
    /// </param>
    /// <returns>
    /// A selection anchored to the page, with no section or node selected.
    /// </returns>
    public static DesignerSelection ForPage(string pageId, DesignerFocusIntent intent) =>
        new() { PageId = pageId, Intent = intent };

    /// <summary>
    /// Selects a repeating group's own drill-in scope with no child selected yet -- an empty
    /// group's own "Edit group fields" entry point, or the fallback a child delete leaves behind
    /// once no sibling survives it (repeating-groups-plan.md, Increment C). A canvas reads
    /// <see cref="NodeId"/> being <see langword="null"/> here as "focus the scope's own heading",
    /// the group-scoped counterpart of <see cref="ForSection"/>'s own node-less fallback.
    /// </summary>
    /// <param name="pageId">
    /// The identifier of the page the group's section belongs to.
    /// </param>
    /// <param name="sectionId">
    /// The identifier of the section the group belongs to.
    /// </param>
    /// <param name="groupId">
    /// The repeating group's own identifier.
    /// </param>
    /// <param name="intent">
    /// Why focus is landing on this scope.
    /// </param>
    /// <returns>
    /// A selection anchored to the group's own scope, with no child node selected.
    /// </returns>
    public static DesignerSelection ForGroupScope(string pageId, string sectionId, string groupId, DesignerFocusIntent intent) =>
        new() { SectionId = sectionId, PageId = pageId, GroupId = groupId, Intent = intent };
}

/// <summary>
/// Why a <see cref="DesignerSelection"/> is where it is, so a later phase's canvas can decide
/// whether and how to move real DOM focus after a mutation (PRD §4.1, §11).
/// </summary>
public enum DesignerFocusIntent
{
    /// <summary>
    /// Focus has not moved as a result of the last change -- e.g. a property edit that keeps the
    /// author on the same field they were already editing.
    /// </summary>
    None,

    /// <summary>
    /// This is a node, section, or page that did not exist a moment ago -- an add, insert, or
    /// duplicate.
    /// </summary>
    NewNode,

    /// <summary>
    /// This is the neighbour a delete fell back to: the next sibling, the previous one if the
    /// deleted node was last, or the owning section when it was the only one.
    /// </summary>
    Neighbour,

    /// <summary>
    /// This is the node that just moved, at its new position.
    /// </summary>
    Moved,

    /// <summary>
    /// This selection was restored from an undo or redo snapshot.
    /// </summary>
    Restored,

    /// <summary>
    /// This selection is the destination of a jump-to-node action -- the linter dock's own way of
    /// naming a finding's node (PRD §8), not a definition mutation of any kind.
    /// </summary>
    JumpedTo,
}
