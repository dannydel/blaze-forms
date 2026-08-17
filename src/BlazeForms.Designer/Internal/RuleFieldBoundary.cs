using BlazeForms.Definitions;
using BlazeForms.Expressions;

namespace BlazeForms.Designer.Internal;

/// <summary>
/// The authoring-time counterpart to the linter's blocking FR-04 rule
/// (repeating-groups-plan.md, Increment C): filters a rule editor's own field-picker candidates so
/// they never offer a reference the linter would go on to reject. Shared by
/// <see cref="Rules.VisibilityRuleEditor"/>, <see cref="Rules.ValidationRuleEditor"/>, and
/// <see cref="Rules.CalculationEditor"/>/<see cref="Rules.CalcOperandRow"/> -- every field picker
/// in the designer.
/// </summary>
/// <remarks>
/// Reference semantics (repeating-groups-plan.md's "Reference semantics" section): a rule or
/// calculation on a node inside a repeating group may reach its own siblings within that same
/// group, or any top-level field, but never another group's children or -- from outside any
/// group -- a group's children at all. <see cref="ExpressionDependencyAnalysis.GetRepeatingGroupOf"/>
/// already knows which group (if any) a node belongs to; this type only ever combines two calls
/// to it into the one boundary check every picker needs.
/// </remarks>
internal static class RuleFieldBoundary
{
    /// <summary>
    /// Filters <paramref name="candidates"/> to the ones <paramref name="editingNodeId"/> may
    /// legally reference.
    /// </summary>
    /// <param name="definition">
    /// The definition both <paramref name="editingNodeId"/> and every candidate belong to.
    /// </param>
    /// <param name="editingNodeId">
    /// The node whose rule or calculation is being authored.
    /// </param>
    /// <param name="candidates">
    /// The candidate fields to filter -- already narrowed by whatever type-specific typing a
    /// caller applies (e.g. <see cref="FormSchema.IsInputNode"/>, a calc operation's own numeric
    /// or date typing).
    /// </param>
    /// <returns>
    /// Every candidate in <paramref name="candidates"/> that is either a top-level field, or a
    /// sibling within <paramref name="editingNodeId"/>'s own repeating group when it has one.
    /// </returns>
    internal static IReadOnlyList<FormNode> Filter(FormDefinition definition, string editingNodeId, IEnumerable<FormNode> candidates)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(editingNodeId);
        ArgumentNullException.ThrowIfNull(candidates);

        var editingGroupId = ExpressionDependencyAnalysis.GetRepeatingGroupOf(definition, editingNodeId);

        return [.. candidates.Where(candidate => IsWithinBoundary(definition, editingGroupId, candidate.Id))];
    }

    private static bool IsWithinBoundary(FormDefinition definition, string? editingGroupId, string candidateNodeId)
    {
        var candidateGroupId = ExpressionDependencyAnalysis.GetRepeatingGroupOf(definition, candidateNodeId);

        // A top-level candidate is always reachable, from anywhere; a candidate inside a group is
        // reachable only from that exact same group -- never from a different group, and never
        // from outside any group at all.
        return candidateGroupId is null || string.Equals(candidateGroupId, editingGroupId, StringComparison.Ordinal);
    }
}
