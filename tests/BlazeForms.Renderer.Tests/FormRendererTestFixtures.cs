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

    /// <summary>
    /// Two pages: page one has two required text fields (<c>first-name</c>, <c>last-name</c>),
    /// page two has one field nobody has to fill in (<c>notes</c>) — for required-blocks-advance,
    /// remedy-wording, and on-blur-validates-only-that-field tests (PRD §4.2, §6).
    /// </summary>
    internal static FormDefinition TwoRequiredFieldsDefinition { get; } = new()
    {
        Id = "form-two-required",
        Name = "Two required",
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
                            new FormNode { Id = "first-name", Type = NodeType.Text, Label = "First name", Required = true },
                            new FormNode { Id = "last-name", Type = NodeType.Text, Label = "Last name", Required = true },
                        ],
                    },
                ],
            },
            new FormPage
            {
                Id = "page-2",
                Title = "Page two",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-2",
                        Title = "Section two",
                        Nodes = [new FormNode { Id = "notes", Type = NodeType.TextArea, Label = "Notes" }],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One page, one section: a checkbox (<c>trigger</c>) controls the visibility of two
    /// dependent fields — <c>detail</c> (hard <see cref="FormNode.Required"/>) and
    /// <c>detail-two</c> (<see cref="FormNode.RequiredWhenVisible"/>). Exercises the rule that a
    /// hidden field is excluded from validation regardless of which required flag it carries,
    /// while either flag blocks once the field is shown (PRD §6).
    /// </summary>
    internal static FormDefinition RequiredWhileVisibleDefinition { get; } = new()
    {
        Id = "form-required-while-visible",
        Name = "Required while visible",
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
                                Label = "Detail",
                                Required = true,
                                VisibleWhen = new ConditionGroup
                                {
                                    Conditions = [new Condition { Field = "trigger", Operator = ConditionOperator.IsTrue }],
                                },
                            },
                            new FormNode
                            {
                                Id = "detail-two",
                                Type = NodeType.Text,
                                Label = "Detail two",
                                RequiredWhenVisible = true,
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
    /// Two pages — <c>start-date</c> on page one, <c>end-date</c> on page two — joined by a
    /// cross-field <see cref="FormDefinition.ValidationRules"/> entry that targets
    /// <c>start-date</c> whenever an end date is given without a start date. Neither field is
    /// individually required, so page-advance never blocks; only a submit's cross-field pass
    /// catches it (PRD §4.2, §6), and the offending field sits on a page other than the one the
    /// respondent is currently on when they submit.
    /// </summary>
    internal static FormDefinition CrossPageValidationDefinition { get; } = new()
    {
        Id = "form-cross-page",
        Name = "Cross page",
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
                        Nodes = [new FormNode { Id = "start-date", Type = NodeType.Date, Label = "Start date" }],
                    },
                ],
            },
            new FormPage
            {
                Id = "page-2",
                Title = "Page two",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-2",
                        Title = "Section two",
                        Nodes = [new FormNode { Id = "end-date", Type = NodeType.Date, Label = "End date" }],
                    },
                ],
            },
        ],
        ValidationRules =
        [
            new ValidationRule
            {
                Target = "start-date",
                Message = "Enter a start date before 'End date'.",
                Expression = new ConditionGroup
                {
                    Conditions =
                    [
                        new Condition { Field = "end-date", Operator = ConditionOperator.IsNotBlank },
                        new Condition { Field = "start-date", Operator = ConditionOperator.IsBlank },
                    ],
                },
            },
        ],
    };

    /// <summary>
    /// One page, three required grouped fields — <see cref="NodeType.YesNo"/>,
    /// <see cref="NodeType.CheckboxGroup"/>, and <see cref="NodeType.DateRange"/> — each with no
    /// control whose own <c>id</c> equals the node's <see cref="FormNode.Id"/>-derived DOM id.
    /// Built for the error-summary-anchors-resolve regression test (PRD §11, WCAG 2.4.1): each
    /// grouped field's own component gives its group container that id instead.
    /// </summary>
    internal static FormDefinition GroupedRequiredFieldsDefinition { get; } = new()
    {
        Id = "form-grouped-required",
        Name = "Grouped required",
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
                            new FormNode
                            {
                                Id = "consent",
                                Type = NodeType.YesNo,
                                Label = "Do you consent?",
                                Required = true,
                                Options =
                                [
                                    new FormOption { Value = "yes", Label = "Yes" },
                                    new FormOption { Value = "no", Label = "No" },
                                ],
                            },
                            new FormNode
                            {
                                Id = "topics",
                                Type = NodeType.CheckboxGroup,
                                Label = "Topics",
                                Required = true,
                                Options =
                                [
                                    new FormOption { Value = "a", Label = "Topic A" },
                                    new FormOption { Value = "b", Label = "Topic B" },
                                ],
                            },
                            new FormNode
                            {
                                Id = "coverage",
                                Type = NodeType.DateRange,
                                Label = "Coverage period",
                                Required = true,
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One page, three fields, none required: <c>name</c> (visible), <c>show-extra</c> (a
    /// checkbox), and <c>extra</c> (visible only while <c>show-extra</c> is checked). Built for
    /// the submission-envelope tests — nothing here blocks validation, so every test using it
    /// exercises the envelope shape and the hidden-field-is-absent rule rather than validation.
    /// </summary>
    internal static FormDefinition SubmissionDefinition { get; } = new()
    {
        Id = "form-submission",
        Name = "Submission",
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
                            new FormNode { Id = "name", Type = NodeType.Text, Label = "Name" },
                            new FormNode { Id = "show-extra", Type = NodeType.Boolean, Label = "Show extra?" },
                            new FormNode
                            {
                                Id = "extra",
                                Type = NodeType.Text,
                                Label = "Extra",
                                VisibleWhen = new ConditionGroup
                                {
                                    Conditions = [new Condition { Field = "show-extra", Operator = ConditionOperator.IsTrue }],
                                },
                            },
                        ],
                    },
                ],
            },
        ],
    };
}
