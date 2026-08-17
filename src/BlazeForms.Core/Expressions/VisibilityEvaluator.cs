using BlazeForms.Definitions;
using BlazeForms.Serialization;

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
/// A <see cref="NodeType.Repeating"/> group's children are never part of that flat walk: they are
/// scoped per row (<see cref="RowScope"/>), not to the outer flat answers, so
/// <see cref="GetVisibleNodes"/> stops descending once it reaches a repeating node — a deliberate
/// behavior change from a form with no repeating groups, where every node was reachable through
/// <see cref="FormNode.Children"/>. <see cref="FilterToVisible"/> instead filters each row of a
/// visible group's <see cref="RepeatingRows"/> value to that row's own visible children via
/// <see cref="GetVisibleChildIds"/>, using the same shrink-only fixed point scoped to the row; a
/// hidden group drops its whole value exactly as any other hidden input node's answer does.
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
    /// Every node whose own rule is satisfied and whose every ancestor is also visible. A
    /// <see cref="NodeType.Repeating"/> node's own children are never included here — they are
    /// scoped per row, not to these flat answers — so use <see cref="GetVisibleChildIds"/> once a
    /// specific row is in hand.
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
    /// Lists the children of one repeating group's row that the respondent currently sees.
    /// </summary>
    /// <param name="repeatingNode">
    /// The repeating group whose <see cref="FormNode.Children"/> to filter.
    /// </param>
    /// <param name="row">
    /// The row to evaluate the children's visibility rules against.
    /// </param>
    /// <param name="outerValues">
    /// The respondent's answers outside the repeating group, keyed by node ID — the base layer a
    /// child's rule falls back to when it names a field outside the group (PRD §5's "Reference
    /// semantics").
    /// </param>
    /// <returns>
    /// The identifiers of every child whose own rule is satisfied against the row-scoped merged
    /// view (<see cref="RowScope.Merge"/>), in <see cref="FormNode.Children"/> order.
    /// </returns>
    public static IReadOnlyList<string> GetVisibleChildIds(
        FormNode repeatingNode,
        RepeatingRow row,
        IReadOnlyDictionary<string, object?> outerValues)
    {
        ArgumentNullException.ThrowIfNull(repeatingNode);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(outerValues);

        return [.. CollectVisibleChildren(repeatingNode, row, outerValues).Select(node => node.Id)];
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
    /// match no node in the definition are dropped rather than nulled. A visible
    /// <see cref="NodeType.Repeating"/> group's <see cref="RepeatingRows"/> value has each row
    /// filtered down to that row's own visible children in turn; a hidden group's whole value is
    /// dropped by the same flat pass that drops any other hidden input node's answer.
    /// </returns>
    public static IReadOnlyDictionary<string, object?> FilterToVisible(
        FormDefinition definition,
        IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);

        return ApplyRowScoping(definition, Settle(definition, values));
    }

    private static Dictionary<string, object?> Settle(
        FormDefinition definition,
        IReadOnlyDictionary<string, object?> values)
    {
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

    /// <summary>
    /// Filters every visible repeating group's rows to their own visible children.
    /// </summary>
    private static Dictionary<string, object?> ApplyRowScoping(
        FormDefinition definition,
        Dictionary<string, object?> values)
    {
        Dictionary<string, object?>? result = null;

        foreach (var node in definition.EnumerateNodes())
        {
            if (node.Type != NodeType.Repeating || node.Children.Count == 0)
            {
                continue;
            }

            if (!values.TryGetValue(node.Id, out var raw) || raw is not RepeatingRows rows)
            {
                continue;
            }

            var filtered = FilterRows(node, rows, values);

            if (ReferenceEquals(filtered, rows))
            {
                continue;
            }

            result ??= new Dictionary<string, object?>(values, StringComparer.Ordinal);
            result[node.Id] = filtered;
        }

        return result ?? values;
    }

    private static RepeatingRows FilterRows(
        FormNode group,
        RepeatingRows rows,
        Dictionary<string, object?> outerValues)
    {
        List<RepeatingRow>? updated = null;

        for (var index = 0; index < rows.Rows.Count; index++)
        {
            var row = rows.Rows[index];
            var filteredValues = FilterRowValues(group, row, outerValues);

            if (filteredValues.Count == row.Values.Count)
            {
                continue;
            }

            updated ??= [.. rows.Rows];
            updated[index] = row with { Values = filteredValues };
        }

        return updated is null ? rows : rows with { Rows = updated };
    }

    private static Dictionary<string, object?> FilterRowValues(
        FormNode group,
        RepeatingRow row,
        Dictionary<string, object?> outerValues)
    {
        // The row-scoped counterpart of Settle's shrink-only fixed point: a rule inside the row
        // may point at a sibling that is itself hidden, so this keeps re-evaluating against what
        // is left until the surviving set stops shrinking.
        var maximumPasses = group.Children.Count + 1;
        IReadOnlyDictionary<string, object?> current = row.Values;

        for (var pass = 0; pass < maximumPasses; pass++)
        {
            var probeRow = row with { Values = current };
            var keep = CollectVisibleChildren(group, probeRow, outerValues)
                .Where(node => FormSchema.IsInputNode(node.Type))
                .Select(node => node.Id)
                .ToHashSet(StringComparer.Ordinal);

            var next = Restrict(current, keep);

            if (next.Count == current.Count)
            {
                return next;
            }

            current = next;
        }

        return Restrict(current, current.Keys.ToHashSet(StringComparer.Ordinal));
    }

    private static List<FormNode> CollectVisibleChildren(
        FormNode repeatingNode,
        RepeatingRow row,
        IReadOnlyDictionary<string, object?> outerValues)
    {
        var merged = RowScope.Merge(outerValues, row);
        var visible = new List<FormNode>();
        CollectVisible(repeatingNode.Children, merged, visible);
        return visible;
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

            if (node.Type == NodeType.Repeating)
            {
                // A repeating group's children are scoped per row (RowScope), never to these
                // flat answers, so the whole-definition walk stops here. This is a deliberate
                // behavior change: use GetVisibleChildIds once a specific row is in hand.
                continue;
            }

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
