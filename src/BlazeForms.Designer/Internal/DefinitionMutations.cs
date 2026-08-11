using BlazeForms.Definitions;
using BlazeForms.Expressions;

namespace BlazeForms.Designer.Internal;

/// <summary>
/// Pure, immutable rebuild helpers over a <see cref="FormDefinition"/> tree -- the only place
/// <see cref="DesignerEditContext"/>'s mutations actually touch the definition's shape. Every
/// method here returns a new <see cref="FormDefinition"/> built with <c>with</c> expressions down
/// the whole page → section → node spine; the definition passed in is never mutated
/// (AGENTS.md invariant #3). Every method that locates something by identifier throws
/// <see cref="ArgumentException"/> when it is not found, rather than silently no-op-ing, because a
/// designer canvas should never be able to hand this class an identifier that is not in the draft
/// it is showing.
/// </summary>
/// <remarks>
/// Operates only on <see cref="FormSection.Nodes"/>, never on <see cref="FormNode.Children"/> --
/// P1 has no editable container node type, so a node's own children are never a mutation target
/// here (PRD §5's <c>repeating</c> is P2-reserved).
/// </remarks>
internal static class DefinitionMutations
{
    /// <summary>
    /// Inserts a new node into a section.
    /// </summary>
    /// <param name="definition">
    /// The definition to insert into.
    /// </param>
    /// <param name="targetSectionId">
    /// The section to insert <paramref name="node"/> into.
    /// </param>
    /// <param name="node">
    /// The node to insert.
    /// </param>
    /// <param name="index">
    /// The zero-based position to insert at, clamped to the section's bounds.
    /// <see langword="null"/> appends to the end.
    /// </param>
    /// <returns>
    /// A new definition with <paramref name="node"/> inserted.
    /// </returns>
    internal static FormDefinition InsertNode(FormDefinition definition, string targetSectionId, FormNode node, int? index)
    {
        var located = RequireSection(definition, targetSectionId);
        var section = definition.Pages[located.PageIndex].Sections[located.SectionIndex];

        var nodes = section.Nodes.ToList();
        var insertAt = Math.Clamp(index ?? nodes.Count, 0, nodes.Count);
        nodes.Insert(insertAt, node);

        return ReplaceSection(definition, located.PageIndex, located.SectionIndex, section with { Nodes = nodes });
    }

    /// <summary>
    /// Removes a node from whichever section holds it.
    /// </summary>
    /// <param name="definition">
    /// The definition to remove from.
    /// </param>
    /// <param name="nodeId">
    /// The node to remove.
    /// </param>
    /// <returns>
    /// A new definition without the node.
    /// </returns>
    internal static FormDefinition RemoveNode(FormDefinition definition, string nodeId)
    {
        var located = RequireNode(definition, nodeId);
        var section = definition.Pages[located.PageIndex].Sections[located.SectionIndex];

        var nodes = section.Nodes.ToList();
        nodes.RemoveAt(located.NodeIndex);

        return ReplaceSection(definition, located.PageIndex, located.SectionIndex, section with { Nodes = nodes });
    }

    /// <summary>
    /// Replaces a node in place, keeping its position.
    /// </summary>
    /// <param name="definition">
    /// The definition to update.
    /// </param>
    /// <param name="updated">
    /// The replacement node. <see cref="FormNode.Id"/> selects which existing node it replaces.
    /// </param>
    /// <returns>
    /// A new definition with the node replaced.
    /// </returns>
    internal static FormDefinition UpdateNode(FormDefinition definition, FormNode updated)
    {
        var located = RequireNode(definition, updated.Id);
        var section = definition.Pages[located.PageIndex].Sections[located.SectionIndex];

        var nodes = section.Nodes.ToList();
        nodes[located.NodeIndex] = updated;

        return ReplaceSection(definition, located.PageIndex, located.SectionIndex, section with { Nodes = nodes });
    }

    /// <summary>
    /// Duplicates a node, inserting the copy immediately after the original in the same section.
    /// </summary>
    /// <param name="definition">
    /// The definition to duplicate within.
    /// </param>
    /// <param name="nodeId">
    /// The node to duplicate.
    /// </param>
    /// <returns>
    /// The new definition, and the duplicate node -- every descendant in its own
    /// <see cref="FormNode.Children"/>, all the way down, gets a fresh
    /// <see cref="FormIds.NewNodeId"/> too, not just the top-level node itself, so a duplicated
    /// subtree (reachable via an imported or otherwise untrusted definition even though P1 has no
    /// editable container node type of its own) never shares an id with the one it was copied
    /// from. Every other property, including each node's own <see cref="FormNode.Options"/>,
    /// carries over untouched so stored option values stay verbatim (AGENTS.md invariant #5).
    /// </returns>
    internal static (FormDefinition Definition, FormNode Duplicate) DuplicateNode(FormDefinition definition, string nodeId)
    {
        var located = RequireNode(definition, nodeId);
        var section = definition.Pages[located.PageIndex].Sections[located.SectionIndex];
        var original = section.Nodes[located.NodeIndex];
        var duplicate = CloneWithFreshIds(original);

        var nodes = section.Nodes.ToList();
        nodes.Insert(located.NodeIndex + 1, duplicate);

        var updated = ReplaceSection(definition, located.PageIndex, located.SectionIndex, section with { Nodes = nodes });
        return (updated, duplicate);
    }

    /// <summary>
    /// Rebuilds <paramref name="node"/> with a fresh <see cref="FormIds.NewNodeId"/> for itself
    /// and, recursively, for every descendant in <see cref="FormNode.Children"/> -- the shared
    /// tail <see cref="DuplicateNode"/> needs so a duplicated subtree never shares an id with the
    /// one it was copied from at any depth. Every other field, including <see cref="FormNode.Options"/>'
    /// own <see cref="FormOption.Value"/>s, carries over verbatim.
    /// </summary>
    /// <param name="node">
    /// The node (and, transitively, its children) to clone with fresh identifiers.
    /// </param>
    /// <returns>
    /// A new <see cref="FormNode"/> identical to <paramref name="node"/> except that it, and
    /// every node in its own descendant tree, carries a freshly minted <see cref="FormNode.Id"/>.
    /// </returns>
    private static FormNode CloneWithFreshIds(FormNode node) =>
        node with
        {
            Id = FormIds.NewNodeId(),
            Children = [.. node.Children.Select(CloneWithFreshIds)],
        };

    /// <summary>
    /// Appends a new, empty page.
    /// </summary>
    /// <param name="definition">
    /// The definition to add to.
    /// </param>
    /// <param name="page">
    /// The page to append.
    /// </param>
    /// <returns>
    /// A new definition with the page appended.
    /// </returns>
    internal static FormDefinition AddPage(FormDefinition definition, FormPage page) =>
        definition with { Pages = [.. definition.Pages, page] };

    /// <summary>
    /// Appends a new, empty section to a page.
    /// </summary>
    /// <param name="definition">
    /// The definition to add to.
    /// </param>
    /// <param name="pageId">
    /// The page to append <paramref name="section"/> to.
    /// </param>
    /// <param name="section">
    /// The section to append.
    /// </param>
    /// <returns>
    /// A new definition with the section appended.
    /// </returns>
    internal static FormDefinition AddSection(FormDefinition definition, string pageId, FormSection section)
    {
        var pageIndex = RequirePage(definition, pageId);
        var page = definition.Pages[pageIndex];

        return ReplacePage(definition, pageIndex, page with { Sections = [.. page.Sections, section] });
    }

    /// <summary>
    /// Moves a node earlier or later within its own section.
    /// </summary>
    /// <param name="definition">
    /// The definition to reorder within.
    /// </param>
    /// <param name="nodeId">
    /// The node to move.
    /// </param>
    /// <param name="delta">
    /// The number of positions to move by; negative moves earlier, positive moves later. Clamped
    /// to the section's bounds -- moving past either end stops at that end rather than wrapping.
    /// </param>
    /// <returns>
    /// A new definition with the node moved, or the exact same <paramref name="definition"/>
    /// instance, unchanged, when the clamped move would not actually change the node's position
    /// (already first and moving earlier, or already last and moving later).
    /// </returns>
    internal static FormDefinition MoveNodeWithinSection(FormDefinition definition, string nodeId, int delta)
    {
        var located = RequireNode(definition, nodeId);
        var section = definition.Pages[located.PageIndex].Sections[located.SectionIndex];
        var newIndex = Math.Clamp(located.NodeIndex + delta, 0, section.Nodes.Count - 1);

        if (newIndex == located.NodeIndex)
        {
            return definition;
        }

        var nodes = section.Nodes.ToList();
        var node = nodes[located.NodeIndex];
        nodes.RemoveAt(located.NodeIndex);
        nodes.Insert(newIndex, node);

        return ReplaceSection(definition, located.PageIndex, located.SectionIndex, section with { Nodes = nodes });
    }

    /// <summary>
    /// Moves a node to a specific zero-based index within a section -- its own, or a different
    /// one. Backs both the drag-and-drop / cross-section reorder path and the "move to position"
    /// dialog path (PRD §4.1); the two differ only in how their caller counts the target index.
    /// </summary>
    /// <param name="definition">
    /// The definition to move within.
    /// </param>
    /// <param name="nodeId">
    /// The node to move.
    /// </param>
    /// <param name="targetSectionId">
    /// The section the node should end up in.
    /// </param>
    /// <param name="index">
    /// The zero-based position within <paramref name="targetSectionId"/> to move to, clamped to
    /// its bounds (after the node leaves its current section, when the two are the same one).
    /// </param>
    /// <returns>
    /// A new definition with the node moved, or the exact same <paramref name="definition"/>
    /// instance, unchanged, when the target section and clamped index are identical to where the
    /// node already is.
    /// </returns>
    internal static FormDefinition MoveNode(FormDefinition definition, string nodeId, string targetSectionId, int index)
    {
        var sourceLocated = RequireNode(definition, nodeId);
        var targetLocated = RequireSection(definition, targetSectionId);
        var sourceSection = definition.Pages[sourceLocated.PageIndex].Sections[sourceLocated.SectionIndex];
        var movingWithinSameSection = sourceLocated.PageIndex == targetLocated.PageIndex
            && sourceLocated.SectionIndex == targetLocated.SectionIndex;

        // The node's own slot stops counting toward the target section's length once it leaves --
        // relevant only when the target is the section it is already in.
        var targetCountAfterRemoval = movingWithinSameSection
            ? sourceSection.Nodes.Count - 1
            : definition.Pages[targetLocated.PageIndex].Sections[targetLocated.SectionIndex].Nodes.Count;
        var clampedIndex = Math.Clamp(index, 0, targetCountAfterRemoval);

        if (movingWithinSameSection && clampedIndex == sourceLocated.NodeIndex)
        {
            return definition;
        }

        var node = sourceSection.Nodes[sourceLocated.NodeIndex];
        var withoutNode = RemoveNode(definition, nodeId);
        var targetAfterRemoval = RequireSection(withoutNode, targetSectionId);
        var targetSection = withoutNode.Pages[targetAfterRemoval.PageIndex].Sections[targetAfterRemoval.SectionIndex];

        var nodes = targetSection.Nodes.ToList();
        nodes.Insert(clampedIndex, node);

        return ReplaceSection(withoutNode, targetAfterRemoval.PageIndex, targetAfterRemoval.SectionIndex, targetSection with { Nodes = nodes });
    }

    /// <summary>
    /// Replaces the form's cross-field validation rules wholesale.
    /// </summary>
    /// <param name="definition">
    /// The definition to update.
    /// </param>
    /// <param name="rules">
    /// The complete replacement rule set.
    /// </param>
    /// <returns>
    /// A new definition carrying <paramref name="rules"/>.
    /// </returns>
    internal static FormDefinition SetValidationRules(FormDefinition definition, IReadOnlyList<ValidationRule> rules) =>
        definition with { ValidationRules = rules };

    /// <summary>
    /// Finds a node's position in the tree.
    /// </summary>
    /// <param name="definition">
    /// The definition to search.
    /// </param>
    /// <param name="nodeId">
    /// The node to find, compared ordinally.
    /// </param>
    /// <returns>
    /// The zero-based page, section, and node indices, or <see langword="null"/> when no section's
    /// top-level <see cref="FormSection.Nodes"/> holds a node with this identifier.
    /// </returns>
    internal static (int PageIndex, int SectionIndex, int NodeIndex)? FindNodeLocation(FormDefinition definition, string nodeId)
    {
        for (var pageIndex = 0; pageIndex < definition.Pages.Count; pageIndex++)
        {
            var sections = definition.Pages[pageIndex].Sections;

            for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
            {
                var nodes = sections[sectionIndex].Nodes;

                for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
                {
                    if (string.Equals(nodes[nodeIndex].Id, nodeId, StringComparison.Ordinal))
                    {
                        return (pageIndex, sectionIndex, nodeIndex);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a page's position in the tree.
    /// </summary>
    /// <param name="definition">
    /// The definition to search.
    /// </param>
    /// <param name="pageId">
    /// The page to find, compared ordinally.
    /// </param>
    /// <returns>
    /// The zero-based page index, or <see langword="null"/> when no page carries this identifier.
    /// </returns>
    internal static int? FindPageIndex(FormDefinition definition, string pageId)
    {
        for (var pageIndex = 0; pageIndex < definition.Pages.Count; pageIndex++)
        {
            if (string.Equals(definition.Pages[pageIndex].Id, pageId, StringComparison.Ordinal))
            {
                return pageIndex;
            }
        }

        return null;
    }

    private static (int PageIndex, int SectionIndex, int NodeIndex) RequireNode(FormDefinition definition, string nodeId) =>
        FindNodeLocation(definition, nodeId)
            ?? throw new ArgumentException($"No node '{nodeId}' was found in the current draft.", nameof(nodeId));

    private static (int PageIndex, int SectionIndex) RequireSection(FormDefinition definition, string sectionId)
    {
        for (var pageIndex = 0; pageIndex < definition.Pages.Count; pageIndex++)
        {
            var sections = definition.Pages[pageIndex].Sections;

            for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
            {
                if (string.Equals(sections[sectionIndex].Id, sectionId, StringComparison.Ordinal))
                {
                    return (pageIndex, sectionIndex);
                }
            }
        }

        throw new ArgumentException($"No section '{sectionId}' was found in the current draft.", nameof(sectionId));
    }

    private static int RequirePage(FormDefinition definition, string pageId) =>
        FindPageIndex(definition, pageId)
            ?? throw new ArgumentException($"No page '{pageId}' was found in the current draft.", nameof(pageId));

    private static FormDefinition ReplaceSection(FormDefinition definition, int pageIndex, int sectionIndex, FormSection newSection)
    {
        var page = definition.Pages[pageIndex];
        var sections = page.Sections.ToList();
        sections[sectionIndex] = newSection;

        return ReplacePage(definition, pageIndex, page with { Sections = sections });
    }

    private static FormDefinition ReplacePage(FormDefinition definition, int pageIndex, FormPage newPage)
    {
        var pages = definition.Pages.ToList();
        pages[pageIndex] = newPage;

        return definition with { Pages = pages };
    }
}
