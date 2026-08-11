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

    /// <summary>
    /// One page, two titled sections, each with two nodes -- enough shape to exercise
    /// <see cref="DesignerEditContext"/>'s within-section and across-section move mutations, and
    /// its delete-neighbour focus fallback.
    /// </summary>
    internal static FormDefinition TwoSectionDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Two section form",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "Details",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-1",
                        Title = "Transportation",
                        Nodes =
                        [
                            new FormNode { Id = "node-a", Type = NodeType.Text, Label = "Field A" },
                            new FormNode { Id = "node-b", Type = NodeType.Text, Label = "Field B" },
                            new FormNode { Id = "node-c", Type = NodeType.Text, Label = "Field C" },
                        ],
                    },
                    new FormSection
                    {
                        Id = "section-2",
                        Title = "Housing",
                        Nodes = [new FormNode { Id = "node-d", Type = NodeType.Text, Label = "Field D" }],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One node carrying options whose <see cref="FormOption.Value"/>s must stay stable under
    /// <see cref="DesignerEditContext.DuplicateNode"/> (AGENTS.md invariant #5).
    /// </summary>
    internal static FormDefinition OptionNodeDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Option node form",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-1",
                        Nodes =
                        [
                            new FormNode
                            {
                                Id = "node-choice",
                                Type = NodeType.Select,
                                Label = "Pick one",
                                Options =
                                [
                                    new FormOption { Value = "opt-1", Label = "Option one" },
                                    new FormOption { Value = "opt-2", Label = "Option two" },
                                ],
                            },
                        ],
                    },
                ],
            },
        ],
    };
}
