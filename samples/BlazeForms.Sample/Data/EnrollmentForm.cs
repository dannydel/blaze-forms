using BlazeForms.Definitions;
using BlazeForms.Expressions;

namespace BlazeForms.Sample.Data;

/// <summary>
/// Builds the sample's reference benefits-enrollment form: three pages, two conditional-visibility
/// branches, and a spread across all 18 P1 node types (PRD §5, §14 success criterion #2). This is
/// the form the Playwright + axe CI gate exercises against real inputs, so every field here is one
/// a respondent could plausibly answer, not a placeholder.
/// </summary>
internal static class EnrollmentForm
{
    /// <summary>
    /// Builds a fresh draft of the reference form, with newly generated ids on every call —
    /// callers publish it once at startup (see <c>Program.cs</c>) and hold onto the result rather
    /// than rebuilding it, since ids are machine-generated and not meant to be recomputed
    /// (AGENTS.md invariant #5).
    /// </summary>
    /// <returns>
    /// An unpublished <see cref="FormDefinition"/> ready to hand to
    /// <c>IFormDefinitionStore.SaveDraftAsync</c> and then <c>PublishAsync</c>.
    /// </returns>
    public static FormDefinition Build()
    {
        // Captured so the visibility rules below can reference the fields they depend on by id.
        var dependentsId = FormIds.NewNodeId();
        var programTypeId = FormIds.NewNodeId();

        return new FormDefinition
        {
            Id = FormIds.NewFormId(),
            Name = "Benefits Enrollment",
            Description = "Reference enrollment form used to exercise the renderer end to end.",
            Program = "benefits",
            Owner = "sample-host",
            Pages =
            [
                BuildApplicantInformationPage(dependentsId),
                BuildCoverageSelectionPage(programTypeId),
                BuildReviewPage(),
            ],
        };
    }

    private static FormPage BuildApplicantInformationPage(string dependentsId)
    {
        var dependentsCountId = FormIds.NewNodeId();

        return new FormPage
        {
            Id = FormIds.NewPageId(),
            Title = "Applicant information",
            Sections =
            [
                new FormSection
                {
                    Id = FormIds.NewSectionId(),
                    Title = "Your details",
                    Description = "We use this to reach you about your enrollment.",
                    Nodes =
                    [
                        new FormNode
                        {
                            Id = FormIds.NewNodeId(),
                            Type = NodeType.Heading,
                            Label = "Personal information",
                            Level = 3,
                        },
                        new FormNode
                        {
                            Id = FormIds.NewNodeId(),
                            Type = NodeType.Paragraph,
                            Content = "Please provide your contact information below. **Fields marked required** are needed to process your enrollment.",
                        },
                        new FormNode
                        {
                            Id = FormIds.NewNodeId(),
                            Type = NodeType.Text,
                            Label = "Full legal name",
                            Required = true,
                        },
                        new FormNode
                        {
                            Id = FormIds.NewNodeId(),
                            Type = NodeType.Email,
                            Label = "Email address",
                            Required = true,
                            Half = true,
                        },
                        new FormNode
                        {
                            Id = FormIds.NewNodeId(),
                            Type = NodeType.Phone,
                            Label = "Phone number",
                            Half = true,
                        },
                        new FormNode
                        {
                            Id = FormIds.NewNodeId(),
                            Type = NodeType.Date,
                            Label = "Date of birth",
                            Required = true,
                        },
                        new FormNode
                        {
                            Id = FormIds.NewNodeId(),
                            Type = NodeType.Divider,
                        },
                    ],
                },
                new FormSection
                {
                    Id = FormIds.NewSectionId(),
                    Title = "Household",
                    Nodes =
                    [
                        new FormNode
                        {
                            Id = dependentsId,
                            Type = NodeType.YesNo,
                            Label = "Do you have any dependents?",
                            Required = true,
                            Options =
                            [
                                new FormOption { Value = "yes", Label = "Yes" },
                                new FormOption { Value = "no", Label = "No" },
                            ],
                        },
                        // Branch 1: only asked once the respondent says they have dependents.
                        new FormNode
                        {
                            Id = dependentsCountId,
                            Type = NodeType.Number,
                            Label = "Number of dependents",
                            Min = 1,
                            Max = 20,
                            RequiredWhenVisible = true,
                            VisibleWhen = new ConditionGroup
                            {
                                Join = ConditionJoin.All,
                                Conditions = [new Condition { Field = dependentsId, Operator = ConditionOperator.Is, Value = "yes" }],
                            },
                        },
                        new FormNode
                        {
                            Id = FormIds.NewNodeId(),
                            Type = NodeType.Callout,
                            Content = "We keep your household information confidential and use it only to determine benefit eligibility.",
                        },
                    ],
                },
            ],
        };
    }

    private static FormPage BuildCoverageSelectionPage(string programTypeId)
    {
        var hardshipCertificationId = FormIds.NewNodeId();
        var hardshipDetailId = FormIds.NewNodeId();

        var visibleWhenHardship = new ConditionGroup
        {
            Join = ConditionJoin.All,
            Conditions = [new Condition { Field = programTypeId, Operator = ConditionOperator.Is, Value = "hardship" }],
        };

        return new FormPage
        {
            Id = FormIds.NewPageId(),
            Title = "Coverage selection",
            Sections =
            [
                new FormSection
                {
                    Id = FormIds.NewSectionId(),
                    Title = "Plan",
                    Nodes =
                    [
                        new FormNode
                        {
                            Id = programTypeId,
                            Type = NodeType.Select,
                            Label = "Program type",
                            Required = true,
                            Options =
                            [
                                new FormOption { Value = "standard", Label = "Standard" },
                                new FormOption { Value = "premium", Label = "Premium" },
                                new FormOption { Value = "hardship", Label = "Hardship waiver" },
                            ],
                        },
                        new FormNode
                        {
                            Id = FormIds.NewNodeId(),
                            Type = NodeType.Radio,
                            Label = "Preferred contact method",
                            Required = true,
                            Options =
                            [
                                new FormOption { Value = "email", Label = "Email" },
                                new FormOption { Value = "phone", Label = "Phone" },
                                new FormOption { Value = "mail", Label = "Mail" },
                            ],
                        },
                        new FormNode
                        {
                            Id = FormIds.NewNodeId(),
                            Type = NodeType.CheckboxGroup,
                            Label = "Which benefits interest you?",
                            Options =
                            [
                                new FormOption { Value = "health", Label = "Health" },
                                new FormOption { Value = "dental", Label = "Dental" },
                                new FormOption { Value = "vision", Label = "Vision" },
                                new FormOption { Value = "life", Label = "Life insurance" },
                            ],
                        },
                        new FormNode
                        {
                            Id = FormIds.NewNodeId(),
                            Type = NodeType.Currency,
                            Label = "Estimated monthly household income",
                            Min = 0,
                            Half = true,
                        },
                        new FormNode
                        {
                            Id = FormIds.NewNodeId(),
                            Type = NodeType.TextArea,
                            Label = "Additional notes for your caseworker",
                        },
                    ],
                },
                new FormSection
                {
                    Id = FormIds.NewSectionId(),
                    Title = "Hardship certification",
                    Description = "Only needed when you select the hardship waiver above.",
                    Nodes =
                    [
                        // Branch 2: both nodes share one rule, so a respondent who never selects
                        // the hardship waiver never sees or is required to answer either.
                        new FormNode
                        {
                            Id = hardshipCertificationId,
                            Type = NodeType.Boolean,
                            Label = "I certify that the information provided demonstrates financial hardship.",
                            RequiredWhenVisible = true,
                            VisibleWhen = visibleWhenHardship,
                        },
                        new FormNode
                        {
                            Id = hardshipDetailId,
                            Type = NodeType.TextArea,
                            Label = "Describe your hardship circumstances",
                            RequiredWhenVisible = true,
                            VisibleWhen = visibleWhenHardship,
                        },
                    ],
                },
            ],
        };
    }

    private static FormPage BuildReviewPage() => new()
    {
        Id = FormIds.NewPageId(),
        Title = "Review and submit",
        Sections =
        [
            new FormSection
            {
                Id = FormIds.NewSectionId(),
                Title = "Enrollment period",
                Nodes =
                [
                    new FormNode
                    {
                        Id = FormIds.NewNodeId(),
                        Type = NodeType.DateRange,
                        Label = "Requested coverage dates",
                        Required = true,
                    },
                    new FormNode
                    {
                        Id = FormIds.NewNodeId(),
                        Type = NodeType.Calc,
                        Label = "Estimated annual total",
                        Placeholder = "Calculated once your enrollment is processed",
                    },
                ],
            },
            new FormSection
            {
                Id = FormIds.NewSectionId(),
                Title = "Confirmation",
                Nodes =
                [
                    new FormNode
                    {
                        Id = FormIds.NewNodeId(),
                        Type = NodeType.Paragraph,
                        Content = "By submitting this form you confirm the information above is accurate to the best of your knowledge.",
                    },
                    new FormNode
                    {
                        Id = FormIds.NewNodeId(),
                        Type = NodeType.Divider,
                    },
                ],
            },
        ],
    };
}
