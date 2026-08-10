using BlazeForms.Definitions;

namespace BlazeForms.Expressions;

/// <summary>
/// Applies <see cref="FormNode.VisibleWhen"/> to a set of answers. Hidden nodes are excluded
/// from validation, from the submission payload, and from the accessibility tree (PRD §6), and
/// hidden answers are <em>absent</em> from the payload rather than null (PRD §9).
/// </summary>
/// <remarks>
/// <para>
/// Two things make visibility more than a per-node predicate. A node inside a hidden container is
/// hidden however its own rule reads, so the walk carries ancestor visibility down. And a rule may
/// point at a field that is itself hidden, so <see cref="FilterToVisible"/> iterates to a fixed
/// point: it drops the answers that are currently hidden, re-evaluates against what is left, and
/// repeats until the set of surviving answers stops shrinking. Without that second pass a chain
/// like a → b → c leaks c's answer into the envelope when a hides b.
/// </para>
/// <para>
/// <see cref="IsVisible"/> is the single-node predicate the designer's logic chip and the
/// renderer's per-field checks use; it knows nothing about ancestors, so prefer
/// <see cref="GetVisibleNodes"/> or <see cref="FilterToVisible"/> whenever the whole form is in
/// hand.
/// </para>
/// </remarks>
public static class VisibilityEvaluator
{
    /// <summary>
    /// Decides whether a node's own rule is satisfied, ignoring its ancestors.
    /// </summary>
    /// <param name="node">
    /// The node to test.
    /// </param>
    /// <param name="values">
    /// The respondent's answers, keyed by node ID.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the node carries no rule, or when its rule is satisfied.
    /// </returns>
    public static bool IsVisible(FormNode node, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(values);

        return node.VisibleWhen is null || ConditionEvaluator.Evaluate(node.VisibleWhen, values);
    }

    /// <summary>
    /// Lists the nodes a respondent currently sees, in definition order.
    /// </summary>
    /// <param name="definition">
    /// The definition being filled.
    /// </param>
    /// <param name="values">
    /// The respondent's answers, keyed by node ID.
    /// </param>
    /// <returns>
    /// Every node whose own rule is satisfied and whose every ancestor is also visible.
    /// </returns>
    public static IReadOnlyList<FormNode> GetVisibleNodes(
        FormDefinition definition,
        IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);

        var visible = new List<FormNode>();

        foreach (var section in definition.Pages.SelectMany(page => page.Sections))
        {
            CollectVisible(section.Nodes, values, visible);
        }

        return visible;
    }

    /// <summary>
    /// Reduces a set of answers to the ones that belong in a submission: answers to input nodes
    /// that are visible once visibility has settled.
    /// </summary>
    /// <param name="definition">
    /// The definition being filled.
    /// </param>
    /// <param name="values">
    /// The respondent's answers, keyed by node ID.
    /// </param>
    /// <returns>
    /// The answers to keep. Answers to hidden nodes — including nodes hidden only because the
    /// field their rule points at is itself hidden — to static content nodes, and to keys that
    /// match no node in the definition are dropped rather than nulled.
    /// </returns>
    public static IReadOnlyDictionary<string, object?> FilterToVisible(
        FormDefinition definition,
        IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);

        // Each pass can only remove answers, so the loop settles in at most one pass per node,
        // plus the final pass that changes nothing.
        var maximumPasses = definition.EnumerateNodes().Count() + 1;
        var current = Restrict(values, VisibleInputNodeIds(definition, values));

        for (var pass = 0; pass < maximumPasses; pass++)
        {
            var next = Restrict(current, VisibleInputNodeIds(definition, current));

            if (next.Count == current.Count)
            {
                return next;
            }

            current = next;
        }

        return current;
    }

    private static void CollectVisible(
        IReadOnlyList<FormNode> nodes,
        IReadOnlyDictionary<string, object?> values,
        List<FormNode> visible)
    {
        foreach (var node in nodes)
        {
            if (!IsVisible(node, values))
            {
                continue;
            }

            visible.Add(node);
            CollectVisible(node.Children, values, visible);
        }
    }

    private static HashSet<string> VisibleInputNodeIds(
        FormDefinition definition,
        IReadOnlyDictionary<string, object?> values) =>
        GetVisibleNodes(definition, values)
            .Where(node => FormSchema.IsInputNode(node.Type))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, object?> Restrict(
        IReadOnlyDictionary<string, object?> values,
        HashSet<string> keep)
    {
        var restricted = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var pair in values)
        {
            if (keep.Contains(pair.Key))
            {
                restricted[pair.Key] = pair.Value;
            }
        }

        return restricted;
    }
}
