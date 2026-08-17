namespace BlazeForms.Definitions;

/// <summary>
/// Facts about the definition schema itself: its version, and which node types belong to which
/// phase (PRD §5, §13).
/// </summary>
public static class FormSchema
{
    /// <summary>
    /// The schema version this build reads and writes. Bumped by any change to the serialized
    /// shape of a definition (AGENTS.md invariant #2). Version 2 added the optional
    /// <see cref="FormNode.Calculation"/> property; version 3 added the optional repeating-group
    /// properties <see cref="FormNode.MinRows"/>, <see cref="FormNode.MaxRows"/>, and
    /// <see cref="FormNode.ItemLabel"/>. An older document omits the newer properties and still
    /// reads, so no migration is needed.
    /// </summary>
    public const int CurrentVersion = 3;

    private static readonly NodeType[] StaticNodeTypesBacking =
    [
        NodeType.Heading,
        NodeType.Paragraph,
        NodeType.Callout,
        NodeType.Divider,
    ];

    // NodeType.Repeating un-reserved for the repeating-groups Designer slice
    // (repeating-groups-plan.md, Increment C): the Core answer model, row-scoped evaluation, and
    // fillable Renderer group all shipped in Increments A and B, so a P1 author can now place one.
    private static readonly NodeType[] ReservedNodeTypesBacking =
    [
        NodeType.File,
        NodeType.Lookup,
    ];

    private static readonly NodeType[] PhaseOneNodeTypesBacking =
        [.. Enum.GetValues<NodeType>().Where(nodeType => !ReservedNodeTypesBacking.Contains(nodeType))];

    /// <summary>
    /// The 19 node types a P1 designer can place on a canvas -- every <see cref="NodeType"/>
    /// except <see cref="ReservedNodeTypes"/>. <see cref="NodeType.Repeating"/> joined this set in
    /// the repeating-groups Designer slice; the name stays <c>PhaseOneNodeTypes</c> since it is
    /// public API, even though a "phase" is no longer a fixed count.
    /// </summary>
    public static IReadOnlyList<NodeType> PhaseOneNodeTypes => PhaseOneNodeTypesBacking;

    /// <summary>
    /// The node types the schema represents but P1 neither edits nor renders. The designer
    /// palette shows them disabled with a phase badge (PRD §5).
    /// </summary>
    public static IReadOnlyList<NodeType> ReservedNodeTypes => ReservedNodeTypesBacking;

    /// <summary>
    /// The node types that carry author content rather than a respondent answer.
    /// </summary>
    public static IReadOnlyList<NodeType> StaticNodeTypes => StaticNodeTypesBacking;

    /// <summary>
    /// Whether nodes of this type capture an answer, and therefore appear in the submission
    /// payload keyed by node ID.
    /// </summary>
    /// <param name="nodeType">
    /// The node type to classify.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for every type except the static content types.
    /// </returns>
    public static bool IsInputNode(NodeType nodeType) => !IsStaticNode(nodeType);

    /// <summary>
    /// Whether nodes of this type render author content and capture nothing.
    /// </summary>
    /// <param name="nodeType">
    /// The node type to classify.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for headings, paragraphs, callouts, and dividers.
    /// </returns>
    public static bool IsStaticNode(NodeType nodeType) => StaticNodeTypesBacking.Contains(nodeType);

    /// <summary>
    /// Whether the schema merely reserves this node type for a later phase.
    /// </summary>
    /// <param name="nodeType">
    /// The node type to classify.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for the file and lookup types.
    /// </returns>
    public static bool IsReservedForLaterPhase(NodeType nodeType) => ReservedNodeTypesBacking.Contains(nodeType);
}
