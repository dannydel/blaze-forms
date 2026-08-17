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
/// <para>
/// <b>A node's own <see cref="FormNode.Children"/> (repeating-groups-plan.md, Increment C).</b>
/// <see cref="RemoveNode"/>, <see cref="UpdateNode"/>, <see cref="DuplicateNode"/>, and
/// <see cref="MoveNodeWithinSection"/> all locate their target by identifier first among
/// <see cref="FormSection.Nodes"/> (a node's own top-level slot) and, only when that search comes
/// up empty, among the <see cref="FormNode.Children"/> of whichever top-level node in that same
/// search owns them -- one level deep, matching the schema's "one nesting level this slice" rule
/// (a repeating group's own children are never themselves a repeating group, so there is never a
/// second level to search). Every one of those four methods therefore applies to a group's child
/// exactly as it does to any other node, with the child's own container (the group's
/// <see cref="FormNode.Children"/>) rebuilt in place of the section's own <see cref="FormSection.Nodes"/>.
/// Only <see cref="InsertNode"/> (which addresses a section directly, with no existing node to
/// locate) and <see cref="MoveNode"/> (which crosses sections) stay section-only -- a palette add
/// scoped into a group instead goes through <see cref="InsertChildNode"/>, and a cross-container
/// move for a child is out of this slice (a child moves only within its own group).
/// </para>
/// <para>
/// <b>No repeating inside a repeating.</b> <see cref="InsertChildNode"/> throws
/// <see cref="ArgumentException"/> for a <see cref="NodeType.Repeating"/> node, defensively --
/// the field palette's own <c>IsInsideRepeatingGroup</c> gate already keeps an author from
/// reaching this path with one, but this class never trusts a caller to have checked.
/// </para>
/// </remarks>
internal static class DefinitionMutations
{
    /// <summary>
    /// One node's location inside a top-level node's own <see cref="FormNode.Children"/> --
    /// <see cref="FindChildLocation"/>'s result, the child-aware counterpart to the
    /// <c>(PageIndex, SectionIndex, NodeIndex)</c> tuple <see cref="FindNodeLocation"/> already
    /// returns for a top-level node.
    /// </summary>
    private readonly record struct ChildLocation
    {
        public required int PageIndex { get; init; }

        public required int SectionIndex { get; init; }

        public required int GroupNodeIndex { get; init; }

        public required int ChildIndex { get; init; }
    }

    /// <summary>
    /// Inserts a new node into a repeating group's own <see cref="FormNode.Children"/> -- the
    /// palette add path while the canvas is scoped into a group (repeating-groups-plan.md,
    /// Increment C). The child-aware counterpart to <see cref="InsertNode"/>, which addresses a
    /// section directly instead.
    /// </summary>
    /// <param name="definition">
    /// The definition to insert into.
    /// </param>
    /// <param name="groupId">
    /// The identifier of the <see cref="NodeType.Repeating"/> node whose <see cref="FormNode.Children"/>
    /// <paramref name="node"/> is inserted into.
    /// </param>
    /// <param name="node">
    /// The node to insert. Never itself <see cref="NodeType.Repeating"/> -- see this type's own
    /// remarks on the no-nested-repeating guard.
    /// </param>
    /// <param name="index">
    /// The zero-based position to insert at, clamped to the group's own child count.
    /// <see langword="null"/> appends to the end.
    /// </param>
    /// <returns>
    /// A new definition with <paramref name="node"/> inserted into <paramref name="groupId"/>'s
    /// own <see cref="FormNode.Children"/>.
    /// </returns>
    internal static FormDefinition InsertChildNode(FormDefinition definition, string groupId, FormNode node, int? index)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Type == NodeType.Repeating)
        {
            throw new ArgumentException("A repeating group's own fields may not include another repeating group.", nameof(node));
        }

        var located = RequireNode(definition, groupId);
        var section = definition.Pages[located.PageIndex].Sections[located.SectionIndex];
        var group = section.Nodes[located.NodeIndex];

        var children = group.Children.ToList();
        var insertAt = Math.Clamp(index ?? children.Count, 0, children.Count);
        children.Insert(insertAt, node);

        var sectionNodes = section.Nodes.ToList();
        sectionNodes[located.NodeIndex] = group with { Children = children };

        return ReplaceSection(definition, located.PageIndex, located.SectionIndex, section with { Nodes = sectionNodes });
    }

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
    /// Removes a node from whichever section -- or, when it is a repeating group's own child,
    /// whichever group's <see cref="FormNode.Children"/> -- holds it (see this type's own remarks).
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
        var located = FindNodeLocation(definition, nodeId);

        if (located is { } topLevel)
        {
            var section = definition.Pages[topLevel.PageIndex].Sections[topLevel.SectionIndex];
            var nodes = section.Nodes.ToList();
            nodes.RemoveAt(topLevel.NodeIndex);

            return ReplaceSection(definition, topLevel.PageIndex, topLevel.SectionIndex, section with { Nodes = nodes });
        }

        var child = RequireChild(definition, nodeId);
        var childSection = definition.Pages[child.PageIndex].Sections[child.SectionIndex];
        var group = childSection.Nodes[child.GroupNodeIndex];

        var children = group.Children.ToList();
        children.RemoveAt(child.ChildIndex);

        var sectionNodes = childSection.Nodes.ToList();
        sectionNodes[child.GroupNodeIndex] = group with { Children = children };

        return ReplaceSection(definition, child.PageIndex, child.SectionIndex, childSection with { Nodes = sectionNodes });
    }

    /// <summary>
    /// Replaces a node in place, keeping its position -- within its own section, or, when it is a
    /// repeating group's own child, within that group's <see cref="FormNode.Children"/> (see this
    /// type's own remarks).
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
        var located = FindNodeLocation(definition, updated.Id);

        if (located is { } topLevel)
        {
            var section = definition.Pages[topLevel.PageIndex].Sections[topLevel.SectionIndex];
            var nodes = section.Nodes.ToList();
            nodes[topLevel.NodeIndex] = updated;

            return ReplaceSection(definition, topLevel.PageIndex, topLevel.SectionIndex, section with { Nodes = nodes });
        }

        var child = RequireChild(definition, updated.Id);
        var childSection = definition.Pages[child.PageIndex].Sections[child.SectionIndex];
        var group = childSection.Nodes[child.GroupNodeIndex];

        var children = group.Children.ToList();
        children[child.ChildIndex] = updated;

        var sectionNodes = childSection.Nodes.ToList();
        sectionNodes[child.GroupNodeIndex] = group with { Children = children };

        return ReplaceSection(definition, child.PageIndex, child.SectionIndex, childSection with { Nodes = sectionNodes });
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
        var located = FindNodeLocation(definition, nodeId);

        if (located is { } topLevel)
        {
            var section = definition.Pages[topLevel.PageIndex].Sections[topLevel.SectionIndex];
            var original = section.Nodes[topLevel.NodeIndex];
            var duplicate = CloneWithFreshIds(original);

            var nodes = section.Nodes.ToList();
            nodes.Insert(topLevel.NodeIndex + 1, duplicate);

            var updated = ReplaceSection(definition, topLevel.PageIndex, topLevel.SectionIndex, section with { Nodes = nodes });
            return (updated, duplicate);
        }

        // A group's own child duplicates within that same group's Children -- one level deep, the
        // only level the schema allows, so CloneWithFreshIds' own recursion never has a nested
        // repeating group of its own to worry about here.
        var child = RequireChild(definition, nodeId);
        var childSection = definition.Pages[child.PageIndex].Sections[child.SectionIndex];
        var group = childSection.Nodes[child.GroupNodeIndex];
        var originalChild = group.Children[child.ChildIndex];
        var duplicateChild = CloneWithFreshIds(originalChild);

        var children = group.Children.ToList();
        children.Insert(child.ChildIndex + 1, duplicateChild);

        var sectionNodes = childSection.Nodes.ToList();
        sectionNodes[child.GroupNodeIndex] = group with { Children = children };

        var updatedDefinition = ReplaceSection(definition, child.PageIndex, child.SectionIndex, childSection with { Nodes = sectionNodes });
        return (updatedDefinition, duplicateChild);
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
    /// Renames a page -- the page tab strip's double-click/F2 inline editor's path.
    /// </summary>
    /// <param name="definition">
    /// The definition to rename within.
    /// </param>
    /// <param name="pageId">
    /// The page to rename.
    /// </param>
    /// <param name="title">
    /// The new title, or <see langword="null"/> to clear it back to the "Page N" fallback.
    /// </param>
    /// <returns>
    /// A new definition with the page's title replaced, or the exact same <paramref name="definition"/>
    /// instance, unchanged, when <paramref name="title"/> is already the page's current title --
    /// the same ReferenceEquals-guard contract <see cref="MoveNodeWithinSection"/> already relies
    /// on, so a caller can skip pushing a no-op onto the undo stack.
    /// </returns>
    internal static FormDefinition RenamePage(FormDefinition definition, string pageId, string? title)
    {
        var pageIndex = RequirePage(definition, pageId);
        var page = definition.Pages[pageIndex];

        if (string.Equals(page.Title, title, StringComparison.Ordinal))
        {
            return definition;
        }

        return ReplacePage(definition, pageIndex, page with { Title = title });
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
        var located = FindNodeLocation(definition, nodeId);

        if (located is { } topLevel)
        {
            var section = definition.Pages[topLevel.PageIndex].Sections[topLevel.SectionIndex];
            var newIndex = Math.Clamp(topLevel.NodeIndex + delta, 0, section.Nodes.Count - 1);

            if (newIndex == topLevel.NodeIndex)
            {
                return definition;
            }

            var nodes = section.Nodes.ToList();
            var node = nodes[topLevel.NodeIndex];
            nodes.RemoveAt(topLevel.NodeIndex);
            nodes.Insert(newIndex, node);

            return ReplaceSection(definition, topLevel.PageIndex, topLevel.SectionIndex, section with { Nodes = nodes });
        }

        // A group's own child reorders within that same group's Children -- its only container,
        // since cross-scope moves (child to section, or to a different group) are out of this
        // slice (see this type's own remarks).
        var child = RequireChild(definition, nodeId);
        var childSection = definition.Pages[child.PageIndex].Sections[child.SectionIndex];
        var group = childSection.Nodes[child.GroupNodeIndex];
        var newChildIndex = Math.Clamp(child.ChildIndex + delta, 0, group.Children.Count - 1);

        if (newChildIndex == child.ChildIndex)
        {
            return definition;
        }

        var children = group.Children.ToList();
        var childNode = children[child.ChildIndex];
        children.RemoveAt(child.ChildIndex);
        children.Insert(newChildIndex, childNode);

        var sectionNodes = childSection.Nodes.ToList();
        sectionNodes[child.GroupNodeIndex] = group with { Children = children };

        return ReplaceSection(definition, child.PageIndex, child.SectionIndex, childSection with { Nodes = sectionNodes });
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
    /// Finds a node's position inside whichever top-level node's own <see cref="FormNode.Children"/>
    /// holds it -- the child-aware counterpart to <see cref="FindNodeLocation"/>, searched only
    /// once <see cref="FindNodeLocation"/> itself has already come up empty (see this type's own
    /// remarks).
    /// </summary>
    /// <param name="definition">
    /// The definition to search.
    /// </param>
    /// <param name="nodeId">
    /// The node to find, compared ordinally.
    /// </param>
    /// <returns>
    /// The child's location, or <see langword="null"/> when no top-level node's own
    /// <see cref="FormNode.Children"/> holds a node with this identifier either.
    /// </returns>
    private static ChildLocation? FindChildLocation(FormDefinition definition, string nodeId)
    {
        for (var pageIndex = 0; pageIndex < definition.Pages.Count; pageIndex++)
        {
            var sections = definition.Pages[pageIndex].Sections;

            for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
            {
                var nodes = sections[sectionIndex].Nodes;

                for (var groupNodeIndex = 0; groupNodeIndex < nodes.Count; groupNodeIndex++)
                {
                    var children = nodes[groupNodeIndex].Children;

                    for (var childIndex = 0; childIndex < children.Count; childIndex++)
                    {
                        if (string.Equals(children[childIndex].Id, nodeId, StringComparison.Ordinal))
                        {
                            return new ChildLocation
                            {
                                PageIndex = pageIndex,
                                SectionIndex = sectionIndex,
                                GroupNodeIndex = groupNodeIndex,
                                ChildIndex = childIndex,
                            };
                        }
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

    /// <summary>
    /// Finds a node's location among a repeating group's own <see cref="FormNode.Children"/>,
    /// throwing when it is in neither a section's own <see cref="FormSection.Nodes"/> nor any
    /// group's <see cref="FormNode.Children"/> -- the shared guard <see cref="RemoveNode"/>,
    /// <see cref="UpdateNode"/>, <see cref="DuplicateNode"/>, and <see cref="MoveNodeWithinSection"/>
    /// all fall back to once <see cref="FindNodeLocation"/> itself comes up empty.
    /// </summary>
    private static ChildLocation RequireChild(FormDefinition definition, string nodeId) =>
        FindChildLocation(definition, nodeId)
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
