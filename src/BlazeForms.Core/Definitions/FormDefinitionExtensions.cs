namespace BlazeForms.Definitions;

/// <summary>
/// Traversal helpers over a definition tree. Every walk descends into
/// <see cref="FormNode.Children"/>, so reserved repeating groups are never silently skipped.
/// </summary>
public static class FormDefinitionExtensions
{
    /// <summary>
    /// Walks every node in the definition, depth-first, in page then section then node order.
    /// </summary>
    /// <param name="definition">
    /// The definition to walk.
    /// </param>
    /// <returns>
    /// Every node in the definition, including nested children.
    /// </returns>
    public static IEnumerable<FormNode> EnumerateNodes(this FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.Pages
            .SelectMany(page => page.Sections)
            .SelectMany(section => section.EnumerateNodes());
    }

    /// <summary>
    /// Walks every node in the section, depth-first.
    /// </summary>
    /// <param name="section">
    /// The section to walk.
    /// </param>
    /// <returns>
    /// Every node in the section, including nested children.
    /// </returns>
    public static IEnumerable<FormNode> EnumerateNodes(this FormSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        return section.Nodes.SelectMany(Flatten);
    }

    /// <summary>
    /// Finds a node by its identifier.
    /// </summary>
    /// <param name="definition">
    /// The definition to search.
    /// </param>
    /// <param name="nodeId">
    /// The node identifier to look for, compared ordinally.
    /// </param>
    /// <returns>
    /// The matching node, or <see langword="null"/> when no node carries that identifier — the
    /// dangling-reference case the linter reports as blocking (PRD §8).
    /// </returns>
    public static FormNode? FindNode(this FormDefinition definition, string nodeId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(nodeId);

        return definition.EnumerateNodes()
            .FirstOrDefault(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Finds the page and section a node belongs to, for the designer's jump-to-node action.
    /// </summary>
    /// <param name="definition">
    /// The definition to search.
    /// </param>
    /// <param name="nodeId">
    /// The node identifier to look for, compared ordinally.
    /// </param>
    /// <returns>
    /// The zero-based page and section indices, or <see langword="null"/> when the node is not
    /// in this definition.
    /// </returns>
    public static (int PageIndex, int SectionIndex)? LocateNode(this FormDefinition definition, string nodeId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(nodeId);

        for (var pageIndex = 0; pageIndex < definition.Pages.Count; pageIndex++)
        {
            var sections = definition.Pages[pageIndex].Sections;

            for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
            {
                var found = sections[sectionIndex]
                    .EnumerateNodes()
                    .Any(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal));

                if (found)
                {
                    return (pageIndex, sectionIndex);
                }
            }
        }

        return null;
    }

    private static IEnumerable<FormNode> Flatten(FormNode node) =>
        node.Children.Count == 0
            ? [node]
            : [node, .. node.Children.SelectMany(Flatten)];
}
