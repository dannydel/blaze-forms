using BlazeForms.Definitions;
using BlazeForms.Expressions;

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
    /// One page, one section, one node exercising every optional flag <c>CanvasNodeRow</c> shows
    /// a chip or rendered content for: required, half-width, a visibility rule, and Markdown
    /// help containing a disallowed <c>javascript:</c> link that must not survive
    /// <c>SafeMarkdown</c> sanitization (AGENTS.md invariant #6).
    /// </summary>
    internal static FormDefinition RichNodeDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Rich node form",
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
                        Title = "Your details",
                        Nodes =
                        [
                            new FormNode
                            {
                                Id = "node-rich",
                                Type = NodeType.Text,
                                Label = "Employer name",
                                Required = true,
                                Half = true,
                                Help = "**Bold** help with an unsafe [link](javascript:alert(1)).",
                                VisibleWhen = new ConditionGroup
                                {
                                    Conditions = [new Condition { Field = "node-rich", Operator = ConditionOperator.Is, Value = "x" }],
                                },
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One page, one section with a single node, and a second, empty section -- exercises Phase
    /// 5's three reorder paths landing on an identical result: appending the sole node to an
    /// empty adjacent section is <c>Alt+→</c>'s own "end of section" choice, the
    /// <c>Ctrl+M</c> dialog's "position 1", and a drag-and-drop onto the empty section's own
    /// container all at once (PRD §4.1).
    /// </summary>
    internal static FormDefinition TwoSectionSecondEmptyDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Two section, second empty, form",
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
                        Nodes = [new FormNode { Id = "node-a", Type = NodeType.Text, Label = "Field A" }],
                    },
                    new FormSection
                    {
                        Id = "section-2",
                        Title = "Housing",
                        Nodes = [],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One node with no label at all -- exercises <c>CanvasNodeRow</c>'s localized
    /// "Untitled {type}" fallback.
    /// </summary>
    internal static FormDefinition UntitledNodeDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Untitled node form",
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
                        Nodes = [new FormNode { Id = "node-untitled", Type = NodeType.Email }],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One numeric node with bounds set -- exercises <c>PropertiesPanel</c>'s Min/Max controls,
    /// shown only for <see cref="NodeType.Number"/>/<see cref="NodeType.Currency"/>.
    /// </summary>
    internal static FormDefinition NumericNodeDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Numeric node form",
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
                        Nodes = [new FormNode { Id = "node-numeric", Type = NodeType.Number, Label = "Quantity", Min = 1, Max = 10 }],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One heading node -- exercises <c>PropertiesPanel</c>'s level select, shown only for
    /// <see cref="NodeType.Heading"/>.
    /// </summary>
    internal static FormDefinition HeadingNodeDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Heading node form",
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
                        Nodes = [new FormNode { Id = "node-heading", Type = NodeType.Heading, Label = "Section title", Level = 3 }],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One paragraph node with Markdown content -- exercises <c>PropertiesPanel</c>'s Content
    /// textarea and "Supports Markdown" marker, shown only for
    /// <see cref="NodeType.Paragraph"/>/<see cref="NodeType.Callout"/>.
    /// </summary>
    internal static FormDefinition ParagraphNodeDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Paragraph node form",
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
                        Nodes = [new FormNode { Id = "node-paragraph", Type = NodeType.Paragraph, Content = "Some **prose**." }],
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
