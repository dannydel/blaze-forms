using BlazeForms.Definitions;
using BlazeForms.Expressions;

namespace BlazeForms.Designer.Internal;

/// <summary>
/// The delete-protection warning's own reference aggregation for a node about to be deleted
/// (repeating-groups-plan.md, Increment C): a plain node's references are exactly
/// <see cref="ExpressionDependencyAnalysis.ReferencesTo"/>'s own result for its own id, but
/// deleting a <see cref="NodeType.Repeating"/> group deletes every one of its own children too
/// (<see cref="Internal.DefinitionMutations.RemoveNode"/> removes the whole node, <see cref="FormNode.Children"/>
/// included), so the warning must name a reference to any of them, not just to the group itself.
/// Shared by <see cref="Canvas.DesignerCanvas.RequestDelete"/> (whether to even open the dialog)
/// and <see cref="Delete.DeleteProtectionDialog"/> (what the dialog actually lists), so the two
/// can never disagree about which delete is "safe".
/// </summary>
internal static class GroupDeleteReferences
{
    /// <summary>
    /// Finds every reference to <paramref name="node"/>, and, when it is a repeating group, to
    /// each of its own <see cref="FormNode.Children"/> as well -- one nesting level being the
    /// schema's own limit, so no further recursion is ever needed.
    /// </summary>
    /// <param name="definition">
    /// The definition to search.
    /// </param>
    /// <param name="node">
    /// The node about to be deleted.
    /// </param>
    /// <returns>
    /// Every reference site, group first then each child in its own <see cref="FormNode.Children"/>
    /// order, matching <see cref="ExpressionDependencyAnalysis.ReferencesTo"/>'s own per-id
    /// ordering within each.
    /// </returns>
    internal static IReadOnlyList<ReferenceSite> ReferencesTo(FormDefinition definition, FormNode node)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(node);

        IReadOnlyList<string> referencedNodeIds = node.Type == NodeType.Repeating
            ? [node.Id, .. node.Children.Select(child => child.Id)]
            : [node.Id];

        // Distinct because a single referencing site can name two members of the same deleted
        // group at once (a validation rule targeting one child while its expression reads another,
        // or one VisibleWhen with two conditions on two children) -- aggregating ReferencesTo per
        // id would otherwise list that one site once per member it names. ReferenceSite is a
        // record, so structural equality collapses those duplicates to the single reference it is.
        return [.. referencedNodeIds
            .SelectMany(id => ExpressionDependencyAnalysis.ReferencesTo(definition, id))
            .Distinct()];
    }
}
