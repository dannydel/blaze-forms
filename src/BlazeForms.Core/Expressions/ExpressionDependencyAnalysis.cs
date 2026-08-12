using BlazeForms.Definitions;

namespace BlazeForms.Expressions;

/// <summary>
/// Where a <see cref="ReferenceSite"/> found a reference to a node — a visibility rule, or one of
/// the two places a cross-field validation rule can name a field (PRD §6).
/// </summary>
public enum ReferenceKind
{
    /// <summary>
    /// A node's own <see cref="FormNode.VisibleWhen"/> names the field via
    /// <see cref="Condition.Field"/>.
    /// </summary>
    Visibility,

    /// <summary>
    /// A <see cref="ValidationRule.Target"/> names the field — the node the rule's message would
    /// be reported against.
    /// </summary>
    ValidationTarget,

    /// <summary>
    /// A <see cref="ValidationRule.Expression"/> names the field via <see cref="Condition.Field"/>.
    /// </summary>
    ValidationExpression,
}

/// <summary>
/// One place a field is referenced from, as found by <see cref="ExpressionDependencyAnalysis.ReferencesTo"/>.
/// Carries enough to both build a human-readable "referenced by …" message and to locate the
/// referencing node or rule for a delete-protection dialog (PRD §4.1's "deleting a field referenced
/// by logic or validation raises a warning naming every reference").
/// </summary>
public sealed record ReferenceSite
{
    /// <summary>
    /// Which kind of reference this is.
    /// </summary>
    public required ReferenceKind Kind { get; init; }

    /// <summary>
    /// The identifier of the node whose own <see cref="FormNode.VisibleWhen"/> carries the
    /// reference. Set only when <see cref="Kind"/> is <see cref="ReferenceKind.Visibility"/>;
    /// <see langword="null"/> for a validation-rule reference.
    /// </summary>
    public string? ReferencingNodeId { get; init; }

    /// <summary>
    /// The validation rule carrying the reference. Set only when <see cref="Kind"/> is
    /// <see cref="ReferenceKind.ValidationTarget"/> or
    /// <see cref="ReferenceKind.ValidationExpression"/>; <see langword="null"/> for a visibility
    /// reference.
    /// </summary>
    public ValidationRule? ReferencingRule { get; init; }
}

/// <summary>
/// Analyzes how fields depend on one another through <see cref="FormNode.VisibleWhen"/> and
/// <see cref="ValidationRule"/> (PRD §6): who references a field (<see cref="ReferencesTo"/>, the
/// foundation Phase 7's delete-protection warning names its every reference from), and whether
/// giving a field a new visibility rule would close a cycle in the visibility graph
/// (<see cref="WouldCreateCycle"/>, "rules are dependency-checked as they are edited; a rule that
/// would create a cycle is rejected with the named path").
/// </summary>
/// <remarks>
/// Every method here is pure and deterministic: neither reads nor writes anything beyond the
/// <see cref="FormDefinition"/> it is handed, so a rule editor can call
/// <see cref="WouldCreateCycle"/> on every candidate edit without any of the caching or
/// invalidation concerns a stateful dependency graph would otherwise need.
/// </remarks>
public static class ExpressionDependencyAnalysis
{
    /// <summary>
    /// Finds every place <paramref name="nodeId"/> is referenced from: another node's
    /// <see cref="FormNode.VisibleWhen"/>, a <see cref="ValidationRule.Target"/>, or a
    /// <see cref="ValidationRule.Expression"/>.
    /// </summary>
    /// <param name="definition">
    /// The definition to search.
    /// </param>
    /// <param name="nodeId">
    /// The identifier of the field to find references to.
    /// </param>
    /// <returns>
    /// Every reference site, in definition order — every node's own visibility rule first (walked
    /// depth-first, page then section then node), then every validation rule, in the order
    /// <see cref="FormDefinition.ValidationRules"/> lists them. Empty when nothing references
    /// <paramref name="nodeId"/>, including when no node in the definition even carries that
    /// identifier — a dangling reference is the linter's concern (FR-03), not this method's.
    /// </returns>
    public static IReadOnlyList<ReferenceSite> ReferencesTo(FormDefinition definition, string nodeId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var sites = new List<ReferenceSite>();

        foreach (var node in definition.EnumerateNodes())
        {
            if (node.VisibleWhen is not null && ReferencesField(node.VisibleWhen, nodeId))
            {
                sites.Add(new ReferenceSite { Kind = ReferenceKind.Visibility, ReferencingNodeId = node.Id });
            }
        }

        foreach (var rule in definition.ValidationRules)
        {
            if (string.Equals(rule.Target, nodeId, StringComparison.Ordinal))
            {
                sites.Add(new ReferenceSite { Kind = ReferenceKind.ValidationTarget, ReferencingRule = rule });
            }

            if (ReferencesField(rule.Expression, nodeId))
            {
                sites.Add(new ReferenceSite { Kind = ReferenceKind.ValidationExpression, ReferencingRule = rule });
            }
        }

        return sites;
    }

    /// <summary>
    /// Decides whether replacing <paramref name="nodeId"/>'s own <see cref="FormNode.VisibleWhen"/>
    /// with <paramref name="candidateVisibleWhen"/> would introduce a cycle in the visibility
    /// graph — the directed graph with one edge <c>nodeId → field</c> for every field a node's own
    /// <see cref="FormNode.VisibleWhen"/> references.
    /// </summary>
    /// <param name="definition">
    /// The definition <paramref name="nodeId"/> belongs to. Every node's <em>current</em>
    /// <see cref="FormNode.VisibleWhen"/> contributes its own edges except <paramref name="nodeId"/>'s
    /// own, which this call replaces with <paramref name="candidateVisibleWhen"/> for the purpose
    /// of this check only — <paramref name="definition"/> itself is never mutated.
    /// </param>
    /// <param name="nodeId">
    /// The field whose visibility rule is being edited.
    /// </param>
    /// <param name="candidateVisibleWhen">
    /// The rule the editor is about to apply, had this check not run.
    /// </param>
    /// <param name="cyclePath">
    /// When this method returns <see langword="true"/>, the cycle as an ordered list of node
    /// identifiers starting and ending at <paramref name="nodeId"/> — e.g. <c>[A, B, C, A]</c> for
    /// a rule on <c>A</c> that transitively depends on itself through <c>B</c> and <c>C</c>, or
    /// <c>[A, A]</c> for a direct self-reference. Empty when this method returns
    /// <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the replacement would close a cycle.
    /// </returns>
    public static bool WouldCreateCycle(
        FormDefinition definition,
        string nodeId,
        ConditionGroup candidateVisibleWhen,
        out IReadOnlyList<string> cyclePath)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(candidateVisibleWhen);

        var edges = BuildVisibilityGraph(definition, nodeId, candidateVisibleWhen);
        var path = new List<string> { nodeId };
        var onPath = new HashSet<string>(StringComparer.Ordinal) { nodeId };

        if (TryFindCycle(nodeId, edges, path, onPath))
        {
            cyclePath = path;
            return true;
        }

        cyclePath = [];
        return false;
    }

    private static bool ReferencesField(ConditionGroup group, string nodeId) =>
        group.Conditions.Any(condition => string.Equals(condition.Field, nodeId, StringComparison.Ordinal));

    /// <summary>
    /// Builds the visibility graph's adjacency list, one entry per node in
    /// <paramref name="definition"/> plus (when it names a node the definition itself does not
    /// contain — a candidate rule authored before the node exists in this exact call's tree is not
    /// a case the designer ever produces, but this stays defensive rather than throwing) an entry
    /// for <paramref name="overriddenNodeId"/> itself. <paramref name="overriddenNodeId"/> always
    /// resolves to <paramref name="overrideGroup"/>'s own fields, regardless of what its current
    /// <see cref="FormNode.VisibleWhen"/> in <paramref name="definition"/> says.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>> BuildVisibilityGraph(
        FormDefinition definition,
        string overriddenNodeId,
        ConditionGroup overrideGroup)
    {
        var graph = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var node in definition.EnumerateNodes())
        {
            var group = string.Equals(node.Id, overriddenNodeId, StringComparison.Ordinal) ? overrideGroup : node.VisibleWhen;
            graph[node.Id] = group is null ? [] : DistinctFields(group);
        }

        if (!graph.ContainsKey(overriddenNodeId))
        {
            graph[overriddenNodeId] = DistinctFields(overrideGroup);
        }

        return graph;
    }

    private static List<string> DistinctFields(ConditionGroup group)
    {
        var fields = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var condition in group.Conditions)
        {
            if (seen.Add(condition.Field))
            {
                fields.Add(condition.Field);
            }
        }

        return fields;
    }

    /// <summary>
    /// The recursive depth-first search over <paramref name="edges"/> that
    /// <see cref="WouldCreateCycle"/> runs from <paramref name="current"/>'s neighbours, building
    /// <paramref name="path"/> as it goes and backtracking out of every branch that never leads
    /// back to <paramref name="path"/>'s own first entry (the node whose rule is being edited).
    /// <paramref name="onPath"/> guards against looping forever around a cycle that does not
    /// involve the start node — reachable only if some other part of the graph was already
    /// cyclic before this call, which every caller that only ever accepts a
    /// <see cref="WouldCreateCycle"/>-approved edit prevents from ever happening in practice.
    /// </summary>
    private static bool TryFindCycle(
        string current,
        Dictionary<string, IReadOnlyList<string>> edges,
        List<string> path,
        HashSet<string> onPath)
    {
        if (!edges.TryGetValue(current, out var neighbors))
        {
            return false;
        }

        foreach (var neighbor in neighbors)
        {
            path.Add(neighbor);

            if (string.Equals(neighbor, path[0], StringComparison.Ordinal))
            {
                // Back to the node whose rule is being edited -- the cycle closes exactly here.
                return true;
            }

            if (onPath.Add(neighbor))
            {
                if (TryFindCycle(neighbor, edges, path, onPath))
                {
                    return true;
                }

                onPath.Remove(neighbor);
            }

            path.RemoveAt(path.Count - 1);
        }

        return false;
    }
}
