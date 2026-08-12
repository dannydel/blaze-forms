using BlazeForms.Definitions;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Builds minimal <see cref="FormNode"/> values for component tests, so each test states only
/// the properties it actually exercises.
/// </summary>
internal static class TestNodes
{
    public static FormNode Create(
        NodeType type,
        string id = "n1",
        string? label = "Label",
        bool required = false,
        string? help = null,
        string? placeholder = null,
        IReadOnlyList<FormOption>? options = null,
        decimal? min = null,
        decimal? max = null,
        int? level = null,
        string? content = null) =>
        new()
        {
            Id = id,
            Type = type,
            Label = label,
            Required = required,
            Help = help,
            Placeholder = placeholder,
            Options = options ?? [],
            Min = min,
            Max = max,
            Level = level,
            Content = content,
        };
}
