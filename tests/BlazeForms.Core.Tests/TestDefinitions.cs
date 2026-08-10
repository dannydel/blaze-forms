using BlazeForms.Definitions;
using BlazeForms.Expressions;

namespace BlazeForms.Core.Tests;

/// <summary>
/// Shared fixtures. <see cref="RepresentativeDefinition"/> deliberately exercises every
/// node type in the schema — including the P2 types the designer only reserves — so the
/// golden file pins the JSON name of each one.
/// </summary>
internal static class TestDefinitions
{
    internal static FormDefinition RepresentativeDefinition { get; } = new()
    {
        Id = "form-transport-enrollment",
        Name = "Student transportation enrollment",
        Description = "Annual request for district-provided student transportation.",
        Program = "Transportation",
        Owner = "transportation-office",
        Pages =
        [
            new FormPage
            {
                Id = "page-student",
                Title = "Student",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-student-details",
                        Title = "Student details",
                        Description = "Tell us who is enrolling.",
                        Nodes =
                        [
                            new FormNode
                            {
                                Id = "node-student-heading",
                                Type = NodeType.Heading,
                                Label = "About the student",
                                Level = 2,
                            },
                            new FormNode
                            {
                                Id = "node-student-intro",
                                Type = NodeType.Paragraph,
                                Content = "Enrollment closes on **1 September**. See the [policy page](https://example.gov/policy).",
                            },
                            new FormNode
                            {
                                Id = "node-first-name",
                                Type = NodeType.Text,
                                Label = "First name",
                                Placeholder = "Ada",
                                Required = true,
                                Half = true,
                            },
                            new FormNode
                            {
                                Id = "node-last-name",
                                Type = NodeType.Text,
                                Label = "Last name",
                                Required = true,
                                Half = true,
                            },
                            new FormNode
                            {
                                Id = "node-date-of-birth",
                                Type = NodeType.Date,
                                Label = "Date of birth",
                                Help = "Use the date on the birth certificate.",
                                Required = true,
                            },
                            new FormNode
                            {
                                Id = "node-grade-level",
                                Type = NodeType.Number,
                                Label = "Grade level",
                                Min = 0m,
                                Max = 12m,
                                Required = true,
                            },
                            new FormNode
                            {
                                Id = "node-annual-fee",
                                Type = NodeType.Currency,
                                Label = "Annual fee",
                                Min = 0m,
                                Max = 1500m,
                            },
                            new FormNode
                            {
                                Id = "node-guardian-email",
                                Type = NodeType.Email,
                                Label = "Guardian email",
                                Required = true,
                            },
                            new FormNode
                            {
                                Id = "node-guardian-phone",
                                Type = NodeType.Phone,
                                Label = "Guardian phone",
                            },
                            new FormNode
                            {
                                Id = "node-student-divider",
                                Type = NodeType.Divider,
                            },
                            new FormNode
                            {
                                Id = "node-residency-notice",
                                Type = NodeType.Callout,
                                Content = "Proof of residency is required for *new* students.",
                            },
                            new FormNode
                            {
                                Id = "node-proof-of-residency",
                                Type = NodeType.File,
                                Label = "Proof of residency",
                                Help = "Upload arrives in a later phase.",
                            },
                            new FormNode
                            {
                                Id = "node-school-code",
                                Type = NodeType.Lookup,
                                Label = "School",
                            },
                        ],
                    },
                ],
            },
            new FormPage
            {
                Id = "page-transportation",
                Title = "Transportation",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-transportation",
                        Title = "Transportation request",
                        Nodes =
                        [
                            new FormNode
                            {
                                Id = "node-needs-transport",
                                Type = NodeType.YesNo,
                                Label = "Does the student need transportation?",
                                Required = true,
                                Options =
                                [
                                    new FormOption { Value = "yes", Label = "Yes" },
                                    new FormOption { Value = "no", Label = "No" },
                                ],
                            },
                            new FormNode
                            {
                                Id = "node-bus-route",
                                Type = NodeType.Select,
                                Label = "Preferred bus route",
                                RequiredWhenVisible = true,
                                Options =
                                [
                                    new FormOption { Value = "route-a", Label = "Route A — North" },
                                    new FormOption { Value = "route-b", Label = "Route B — South" },
                                ],
                                VisibleWhen = new ConditionGroup
                                {
                                    Join = ConditionJoin.All,
                                    Conditions =
                                    [
                                        new Condition
                                        {
                                            Field = "node-needs-transport",
                                            Operator = ConditionOperator.Is,
                                            Value = "yes",
                                        },
                                    ],
                                },
                            },
                            new FormNode
                            {
                                Id = "node-pickup-type",
                                Type = NodeType.Radio,
                                Label = "Pickup location",
                                Options =
                                [
                                    new FormOption { Value = "home", Label = "Home" },
                                    new FormOption { Value = "stop", Label = "Neighbourhood stop" },
                                ],
                                VisibleWhen = new ConditionGroup
                                {
                                    Join = ConditionJoin.Any,
                                    Conditions =
                                    [
                                        new Condition
                                        {
                                            Field = "node-needs-transport",
                                            Operator = ConditionOperator.Is,
                                            Value = "yes",
                                        },
                                        new Condition
                                        {
                                            Field = "node-grade-level",
                                            Operator = ConditionOperator.LessThan,
                                            Value = "3",
                                        },
                                    ],
                                },
                            },
                            new FormNode
                            {
                                Id = "node-accommodations",
                                Type = NodeType.CheckboxGroup,
                                Label = "Accommodations needed",
                                Options =
                                [
                                    new FormOption { Value = "lift", Label = "Wheelchair lift" },
                                    new FormOption { Value = "aide", Label = "Riding aide" },
                                    new FormOption { Value = "harness", Label = "Safety harness" },
                                ],
                            },
                            new FormNode
                            {
                                Id = "node-service-window",
                                Type = NodeType.DateRange,
                                Label = "Service window",
                            },
                            new FormNode
                            {
                                Id = "node-notes",
                                Type = NodeType.TextArea,
                                Label = "Anything else we should know?",
                                Placeholder = "Optional",
                            },
                            new FormNode
                            {
                                Id = "node-consent",
                                Type = NodeType.Boolean,
                                Label = "I confirm the information above is accurate",
                                Required = true,
                            },
                            new FormNode
                            {
                                Id = "node-estimated-cost",
                                Type = NodeType.Calc,
                                Label = "Estimated annual cost",
                                Help = "Calculated once the calc engine ships.",
                            },
                            new FormNode
                            {
                                Id = "node-siblings",
                                Type = NodeType.Repeating,
                                Label = "Siblings also enrolling",
                                Children =
                                [
                                    new FormNode
                                    {
                                        Id = "node-sibling-name",
                                        Type = NodeType.Text,
                                        Label = "Sibling name",
                                    },
                                ],
                            },
                        ],
                    },
                ],
            },
        ],
        ValidationRules =
        [
            new ValidationRule
            {
                Target = "node-bus-route",
                Message = "Choose a bus route, or answer No to the transportation question.",
                Expression = new ConditionGroup
                {
                    Join = ConditionJoin.All,
                    Conditions =
                    [
                        new Condition
                        {
                            Field = "node-needs-transport",
                            Operator = ConditionOperator.Is,
                            Value = "yes",
                        },
                        new Condition
                        {
                            Field = "node-bus-route",
                            Operator = ConditionOperator.IsBlank,
                        },
                    ],
                },
            },
        ],
    };

    private static FormDefinition SinglePage(string id, params FormNode[] nodes) => new()
    {
        Id = id,
        Name = id,
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "Page one",
                Sections = [new FormSection { Id = "section-1", Title = "Section one", Nodes = nodes }],
            },
        ],
    };

    private static ConditionGroup ShownWhenYes(string field) => new()
    {
        Conditions = [new Condition { Field = field, Operator = ConditionOperator.Is, Value = "yes" }],
    };

    /// <summary>
    /// A three-link visibility chain, a → b → c, for the cascade tests: <c>node-b</c> shows while
    /// <c>node-a</c> is <c>yes</c>, and <c>node-c</c> shows while <c>node-b</c> is <c>yes</c>.
    /// </summary>
    internal static FormDefinition ChainedDefinition { get; } = SinglePage(
        "form-chained",
        new FormNode { Id = "node-a", Type = NodeType.YesNo, Label = "A" },
        new FormNode
        {
            Id = "node-b",
            Type = NodeType.YesNo,
            Label = "B",
            VisibleWhen = ShownWhenYes("node-a"),
        },
        new FormNode
        {
            Id = "node-c",
            Type = NodeType.Text,
            Label = "C",
            VisibleWhen = ShownWhenYes("node-b"),
        });

    /// <summary>
    /// A conditional container holding an unconditional child, for the ancestor-propagation tests.
    /// </summary>
    internal static FormDefinition NestedDefinition { get; } = SinglePage(
        "form-nested",
        new FormNode { Id = "node-has-siblings", Type = NodeType.YesNo, Label = "Any siblings?" },
        new FormNode
        {
            Id = "node-siblings",
            Type = NodeType.Repeating,
            Label = "Siblings",
            VisibleWhen = ShownWhenYes("node-has-siblings"),
            Children =
            [
                new FormNode { Id = "node-sibling-name", Type = NodeType.Text, Label = "Sibling name" },
            ],
        });

    /// <summary>
    /// A two-field definition used by the visibility tests: <c>node-detail</c> shows only
    /// while <c>node-trigger</c> holds the stored value <c>yes</c>.
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
                            new FormNode
                            {
                                Id = "node-trigger",
                                Type = NodeType.YesNo,
                                Label = "Need help?",
                            },
                            new FormNode
                            {
                                Id = "node-detail",
                                Type = NodeType.Text,
                                Label = "Describe the help you need",
                                RequiredWhenVisible = true,
                                VisibleWhen = new ConditionGroup
                                {
                                    Conditions =
                                    [
                                        new Condition
                                        {
                                            Field = "node-trigger",
                                            Operator = ConditionOperator.Is,
                                            Value = "yes",
                                        },
                                    ],
                                },
                            },
                            new FormNode
                            {
                                Id = "node-static-heading",
                                Type = NodeType.Heading,
                                Label = "Static content carries no value",
                                Level = 2,
                            },
                        ],
                    },
                ],
            },
        ],
    };
}
