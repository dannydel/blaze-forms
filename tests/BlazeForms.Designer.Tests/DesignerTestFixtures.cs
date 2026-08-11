using BlazeForms.Definitions;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Shared <see cref="FormDefinition"/> fixtures for designer tests, mirroring the style of
/// <c>BlazeForms.Renderer.Tests.FormRendererTestFixtures</c> but sized to what
/// <see cref="FormDesigner"/>'s shell needs.
/// </summary>
internal static class DesignerTestFixtures
{
    /// <summary>
    /// One page with one section holding a single text field — just enough content for
    /// <see cref="FormDesigner"/>'s canvas pane to have a name to show.
    /// </summary>
    internal static FormDefinition OneFieldDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Reference enrollment form",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "About you",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-1",
                        Title = "Your details",
                        Nodes = [new FormNode { Id = "first-name", Type = NodeType.Text, Label = "First name" }],
                    },
                ],
            },
        ],
    };
}
