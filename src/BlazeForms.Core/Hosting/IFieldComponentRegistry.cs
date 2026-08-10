using BlazeForms.Definitions;

namespace BlazeForms.Hosting;

/// <summary>
/// Maps field types to the host's own components, so a host can swap in its design system's
/// input for a given node type without touching Core or the renderer (PRD §10, D3).
/// </summary>
/// <remarks>
/// The contract deals in <see cref="Type"/> deliberately. Naming a component base type here
/// would drag a UI dependency into Core and break the agnosticism invariant (AGENTS.md #1),
/// which the architecture test enforces. The renderer is what requires the resolved type to be a
/// Blazor component; hosts that register something else get a renderer-side failure, not a
/// Core-side one.
/// </remarks>
public interface IFieldComponentRegistry
{
    /// <summary>
    /// Looks up the host component registered for a field type.
    /// </summary>
    /// <param name="nodeType">
    /// The field type to resolve.
    /// </param>
    /// <param name="componentType">
    /// The registered component type, or <see langword="null"/> when the host has registered
    /// none and the shipped default should be used.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the host has registered an override for this field type.
    /// </returns>
    bool TryGetComponentType(NodeType nodeType, out Type? componentType);
}
