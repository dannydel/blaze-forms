using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Versioning;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Shared <see cref="FormDefinition"/>/<see cref="FormVersion"/> fixtures for
/// <see cref="FormRenderer"/> tests, mirroring the style of
/// <c>BlazeForms.Core.Tests.TestDefinitions</c> but sized to what each renderer behavior needs.
/// </summary>
internal static class FormRendererTestFixtures
{
    /// <summary>
    /// Wraps a definition as the published <see cref="FormVersion"/> the renderer's
    /// <see cref="FormRenderer.Version"/> parameter takes.
    /// </summary>
    internal static FormVersion ToPublishedVersion(FormDefinition definition) => new()
    {
        FormId = definition.Id,
        Version = 1,
        State = FormLifecycleState.Published,
        Definition = definition,
    };

    /// <summary>
    /// Two pages, each with one section: page one has two half-width text fields, page two has
    /// a single text area — enough to exercise step navigation, the progress header, and the
    /// half/full column layout.
    /// </summary>
    internal static FormDefinition TwoStepDefinition { get; } = new()
    {
        Id = "form-two-step",
        Name = "Two step",
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
                        Description = "We use this to reach you.",
                        Nodes =
                        [
                            new FormNode { Id = "first-name", Type = NodeType.Text, Label = "First name", Half = true },
                            new FormNode { Id = "last-name", Type = NodeType.Text, Label = "Last name", Half = true },
                        ],
                    },
                ],
            },
            new FormPage
            {
                Id = "page-2",
                Title = "Preferences",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-2",
                        Title = "How can we help?",
                        Nodes = [new FormNode { Id = "notes", Type = NodeType.TextArea, Label = "Notes" }],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One page, one section: a single checkbox (<c>trigger</c>) controls whether a text field
    /// (<c>detail</c>) is visible.
    /// </summary>
    internal static FormDefinition ConditionalDefinition { get; } = new()
    {
        Id = "form-conditional",
        Name = "Conditional",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "Page one",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-1",
                        Title = "Section one",
                        Nodes =
                        [
                            new FormNode { Id = "trigger", Type = NodeType.Boolean, Label = "Need help?" },
                            new FormNode
                            {
                                Id = "detail",
                                Type = NodeType.Text,
                                Label = "Describe the help you need",
                                VisibleWhen = new ConditionGroup
                                {
                                    Conditions = [new Condition { Field = "trigger", Operator = ConditionOperator.IsTrue }],
                                },
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// A two-level visibility chain on one page: a boolean (<c>expert</c>) controls a number
    /// (<c>years</c>, <c>visibleWhen expert isTrue</c>), which in turn controls a text field
    /// (<c>senior-notice</c>, <c>visibleWhen years gt 10</c>). Exercises the fixed-point path —
    /// hiding <c>expert</c> must drop <c>years</c>'s stale answer so <c>senior-notice</c> hides
    /// too, rather than the leaf lingering on a value no longer reachable (PRD §6).
    /// </summary>
    internal static FormDefinition ChainedVisibilityDefinition { get; } = new()
    {
        Id = "form-chained",
        Name = "Chained",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "Page one",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-1",
                        Title = "Section one",
                        Nodes =
                        [
                            new FormNode { Id = "expert", Type = NodeType.Boolean, Label = "Expert mode" },
                            new FormNode
                            {
                                Id = "years",
                                Type = NodeType.Number,
                                Label = "Years of experience",
                                VisibleWhen = new ConditionGroup
                                {
                                    Conditions = [new Condition { Field = "expert", Operator = ConditionOperator.IsTrue }],
                                },
                            },
                            new FormNode
                            {
                                Id = "senior-notice",
                                Type = NodeType.Text,
                                Label = "Senior-rate notice",
                                VisibleWhen = new ConditionGroup
                                {
                                    Conditions = [new Condition { Field = "years", Operator = ConditionOperator.GreaterThan, Value = "10" }],
                                },
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One page, one section, two independent text fields — for asserting that changing one
    /// field's answer never re-renders the other.
    /// </summary>
    internal static FormDefinition TwoFieldDefinition { get; } = new()
    {
        Id = "form-two-field",
        Name = "Two field",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "Page one",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-1",
                        Title = "Section one",
                        Nodes =
                        [
                            new FormNode { Id = "field-a", Type = NodeType.Text, Label = "Field A" },
                            new FormNode { Id = "field-b", Type = NodeType.Text, Label = "Field B" },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One page, one section, a single <see cref="NodeType.Calc"/> node.
    /// </summary>
    internal static FormDefinition CalcDefinition { get; } = new()
    {
        Id = "form-calc",
        Name = "Calc",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "Page one",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-1",
                        Title = "Section one",
                        Nodes = [new FormNode { Id = "estimate", Type = NodeType.Calc, Label = "Estimate" }],
                    },
                ],
            },
        ],
    };
}
