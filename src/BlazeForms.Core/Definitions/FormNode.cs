using BlazeForms.Expressions;

namespace BlazeForms.Definitions;

/// <summary>
/// One node on a form: an input, a static content block, or a container. Nodes are values —
/// every edit produces a new instance — and <see cref="Id"/> is the only thing that ever keys
/// an answer (AGENTS.md invariant #5).
/// </summary>
public sealed record FormNode
{
    private readonly IReadOnlyList<FormOption>? _options;
    private readonly IReadOnlyList<FormNode>? _children;

    /// <summary>
    /// The machine-generated, immutable identifier that keys this node's answer. Generate one
    /// with <see cref="FormIds.NewNodeId"/>; never derive it from <see cref="Label"/>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The kind of node, which decides how it renders and what it captures.
    /// </summary>
    public required NodeType Type { get; init; }

    /// <summary>
    /// The display label. Plain text always (PRD §5.1) — it feeds <c>legend</c> and <c>aria</c>
    /// contexts and is quoted inside validation messages.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// Supporting text shown with the input. Markdown-enabled (PRD §5.1).
    /// </summary>
    public string? Help { get; init; }

    /// <summary>
    /// Placeholder text for the input. Plain text, and never a substitute for
    /// <see cref="Label"/>.
    /// </summary>
    public string? Placeholder { get; init; }

    /// <summary>
    /// The prose carried by a <see cref="NodeType.Paragraph"/> or
    /// <see cref="NodeType.Callout"/> node. Markdown-enabled (PRD §5.1).
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Whether an answer is always required.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// Whether an answer is required only while the node is visible (PRD §6). Ignored when
    /// <see cref="Required"/> is already set.
    /// </summary>
    public bool RequiredWhenVisible { get; init; }

    /// <summary>
    /// Whether the node occupies half the available width on wide viewports. Half-width pairs
    /// collapse to a single column on narrow ones.
    /// </summary>
    public bool Half { get; init; }

    /// <summary>
    /// The choices offered by a <see cref="NodeType.Select"/>,
    /// <see cref="NodeType.Radio"/>, <see cref="NodeType.CheckboxGroup"/>, or
    /// <see cref="NodeType.YesNo"/> node. Stored values stay stable when labels are edited.
    /// </summary>
    public IReadOnlyList<FormOption> Options
    {
        get => _options ?? [];
        init => _options = value is null ? null : Array.AsReadOnly<FormOption>([.. value]);
    }

    /// <summary>
    /// The inclusive lower bound for a <see cref="NodeType.Number"/> or
    /// <see cref="NodeType.Currency"/> node.
    /// </summary>
    public decimal? Min { get; init; }

    /// <summary>
    /// The inclusive upper bound for a <see cref="NodeType.Number"/> or
    /// <see cref="NodeType.Currency"/> node.
    /// </summary>
    public decimal? Max { get; init; }

    /// <summary>
    /// The heading rung, 2 to 4, for a <see cref="NodeType.Heading"/> node.
    /// </summary>
    public int? Level { get; init; }

    /// <summary>
    /// The nodes repeated by a <see cref="NodeType.Repeating"/> node. Reserved for P2; empty
    /// for every P1 node type.
    /// </summary>
    public IReadOnlyList<FormNode> Children
    {
        get => _children ?? [];
        init => _children = value is null ? null : Array.AsReadOnly<FormNode>([.. value]);
    }

    /// <summary>
    /// The rule that decides whether this node is shown. <see langword="null"/> means always
    /// visible. A hidden node is excluded from validation, from the submission payload, and
    /// from the accessibility tree (PRD §6).
    /// </summary>
    public ConditionGroup? VisibleWhen { get; init; }
}
