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
    /// One page, one section, a single <see cref="NodeType.Calc"/> node whose expression is a
    /// fixed numeric literal (no field dependency) — enough for a component-wiring test to prove
    /// the field actually receives a formatted <see cref="Fields.FormFieldBase.Value"/>, with
    /// nothing else in the fixture able to change it.
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
                        Nodes =
                        [
                            new FormNode
                            {
                                Id = "estimate",
                                Type = NodeType.Calc,
                                Label = "Estimate",
                                Calculation = new CalcExpression
                                {
                                    Operation = CalcOperation.Sum,
                                    Operands = [new CalcOperand { Number = 42m }],
                                    Format = CalcFormat.Number,
                                },
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One page, one section: two independent number fields (<c>a</c>, <c>b</c>) and a calc
    /// (<c>total</c>) whose expression reads only <c>a</c> — <c>b</c> is a red herring, present
    /// solely so a test can prove typing into it never recomputes or re-renders <c>total</c>.
    /// </summary>
    internal static FormDefinition CalcDependsOnOneOfTwoFieldsDefinition { get; } = new()
    {
        Id = "form-calc-dependency",
        Name = "Calc dependency",
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
                            new FormNode { Id = "a", Type = NodeType.Number, Label = "A" },
                            new FormNode { Id = "b", Type = NodeType.Number, Label = "B" },
                            new FormNode
                            {
                                Id = "total",
                                Type = NodeType.Calc,
                                Label = "Total",
                                Calculation = new CalcExpression
                                {
                                    Operation = CalcOperation.Sum,
                                    Operands = [new CalcOperand { Field = "a" }],
                                    Format = CalcFormat.Number,
                                },
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One page, one section: a boolean (<c>trigger</c>) controls the visibility of a calc
    /// (<c>hidden-total</c>, a fixed numeric literal so it always has a value once computed) —
    /// for proving a hidden calc's captured value is filtered out of the envelope exactly like
    /// any other hidden input node.
    /// </summary>
    internal static FormDefinition CalcHiddenByVisibilityDefinition { get; } = new()
    {
        Id = "form-calc-hidden",
        Name = "Calc hidden",
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
                            new FormNode { Id = "trigger", Type = NodeType.Boolean, Label = "Show total?" },
                            new FormNode
                            {
                                Id = "hidden-total",
                                Type = NodeType.Calc,
                                Label = "Total",
                                VisibleWhen = new ConditionGroup
                                {
                                    Conditions = [new Condition { Field = "trigger", Operator = ConditionOperator.IsTrue }],
                                },
                                Calculation = new CalcExpression
                                {
                                    Operation = CalcOperation.Sum,
                                    Operands = [new CalcOperand { Number = 10m }],
                                    Format = CalcFormat.Number,
                                },
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One page, one section: a number field (<c>amount</c>) feeds a calc (<c>total</c>), and a
    /// text field (<c>bonus-notice</c>) is visible only once <c>total</c> exceeds 100 — proving a
    /// visibility rule can target a calc node's computed value, not just a respondent-typed one.
    /// </summary>
    internal static FormDefinition CalcFeedsVisibilityDefinition { get; } = new()
    {
        Id = "form-calc-feeds-visibility",
        Name = "Calc feeds visibility",
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
                            new FormNode { Id = "amount", Type = NodeType.Number, Label = "Amount" },
                            new FormNode
                            {
                                Id = "total",
                                Type = NodeType.Calc,
                                Label = "Total",
                                Calculation = new CalcExpression
                                {
                                    Operation = CalcOperation.Sum,
                                    Operands = [new CalcOperand { Field = "amount" }],
                                    Format = CalcFormat.Number,
                                },
                            },
                            new FormNode
                            {
                                Id = "bonus-notice",
                                Type = NodeType.Text,
                                Label = "Bonus notice",
                                VisibleWhen = new ConditionGroup
                                {
                                    Conditions = [new Condition { Field = "total", Operator = ConditionOperator.GreaterThan, Value = "100" }],
                                },
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One page, one section, a single <c>today()</c>-only calc (<c>today-date</c>): its
    /// expression reads no field at all, only <see cref="CalcFunction.Today"/>, via a
    /// zero-day <see cref="CalcOperation.DateAddDays"/> that hands the date straight back
    /// unchanged — for proving the renderer's injected <see cref="TimeProvider"/> resolves
    /// deterministically rather than reading the real clock.
    /// </summary>
    internal static FormDefinition CalcTodayOnlyDefinition { get; } = new()
    {
        Id = "form-calc-today",
        Name = "Calc today",
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
                                Id = "today-date",
                                Type = NodeType.Calc,
                                Label = "Today",
                                Calculation = new CalcExpression
                                {
                                    Operation = CalcOperation.DateAddDays,
                                    Operands = [new CalcOperand { Function = CalcFunction.Today }, new CalcOperand { Number = 0m }],
                                    Format = CalcFormat.Date,
                                },
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// Two pages: page one has one unrelated text field (<c>note</c>); page two has a number
    /// field (<c>amount</c>) and a calc (<c>total</c>) that sums it -- for proving the calc
    /// announcer only ever names a calc that lives on the CURRENT page (code review fix #4).
    /// </summary>
    internal static FormDefinition CalcOnSecondPageDefinition { get; } = new()
    {
        Id = "form-calc-second-page",
        Name = "Calc on second page",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "Page one",
                Sections = [new FormSection { Id = "section-1", Title = "Section one", Nodes = [new FormNode { Id = "note", Type = NodeType.Text, Label = "Note" }] }],
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
                        Nodes =
                        [
                            new FormNode { Id = "amount", Type = NodeType.Number, Label = "Amount" },
                            new FormNode
                            {
                                Id = "total",
                                Type = NodeType.Calc,
                                Label = "Total",
                                Calculation = new CalcExpression
                                {
                                    Operation = CalcOperation.Sum,
                                    Operands = [new CalcOperand { Field = "amount" }],
                                    Format = CalcFormat.Number,
                                },
                            },
                        ],
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
    /// <summary>
    /// One page, one section, a single repeating group (<c>siblings</c>, <c>ItemLabel</c>
    /// "Sibling", <c>MinRows</c> 1, <c>MaxRows</c> 3) whose children are a text field
    /// (<c>sibling-name</c>, required), a number field (<c>sibling-age</c>), and a calc
    /// (<c>sibling-age-plus-one</c>, sums <c>sibling-age</c> and 1) — enough to exercise add,
    /// remove, reorder, per-row required validation, and per-row calc (D-3).
    /// </summary>
    internal static FormDefinition RepeatingDefinition { get; } = new()
    {
        Id = "form-repeating",
        Name = "Repeating",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "Household",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-1",
                        Title = "Members",
                        Nodes =
                        [
                            new FormNode
                            {
                                Id = "siblings",
                                Type = NodeType.Repeating,
                                Label = "Siblings",
                                ItemLabel = "Sibling",
                                MinRows = 1,
                                MaxRows = 3,
                                Children =
                                [
                                    new FormNode { Id = "sibling-name", Type = NodeType.Text, Label = "Sibling name", Required = true },
                                    new FormNode { Id = "sibling-age", Type = NodeType.Number, Label = "Sibling age" },
                                    new FormNode
                                    {
                                        Id = "sibling-age-plus-one",
                                        Type = NodeType.Calc,
                                        Label = "Sibling age plus one",
                                        Calculation = new CalcExpression
                                        {
                                            Operation = CalcOperation.Sum,
                                            Operands = [new CalcOperand { Field = "sibling-age" }, new CalcOperand { Number = 1m }],
                                            Format = CalcFormat.Number,
                                        },
                                    },
                                ],
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// A repeating group (<c>siblings</c>) whose <c>MinRows</c> is unset (0), one required text
    /// child (<c>sibling-name</c>), a boolean controller (<c>wants-notes</c>), and a text field
    /// (<c>sibling-notes</c>) visible only within a row whose own <c>wants-notes</c> is true —
    /// for within-row visibility and hidden-in-row-child capture-at-submit tests.
    /// </summary>
    internal static FormDefinition RepeatingWithinRowVisibilityDefinition { get; } = new()
    {
        Id = "form-repeating-visibility",
        Name = "Repeating visibility",
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
                                Id = "siblings",
                                Type = NodeType.Repeating,
                                Label = "Siblings",
                                ItemLabel = "Sibling",
                                Children =
                                [
                                    new FormNode { Id = "sibling-name", Type = NodeType.Text, Label = "Sibling name", Required = true },
                                    new FormNode { Id = "wants-notes", Type = NodeType.Boolean, Label = "Add notes?" },
                                    new FormNode
                                    {
                                        Id = "sibling-notes",
                                        Type = NodeType.TextArea,
                                        Label = "Notes",
                                        VisibleWhen = new ConditionGroup
                                        {
                                            Conditions = [new Condition { Field = "wants-notes", Operator = ConditionOperator.IsTrue }],
                                        },
                                    },
                                ],
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// A repeating group (<c>siblings</c>) hidden behind a top-level controller
    /// (<c>has-siblings</c>) — for the hidden-group capture-at-submit test.
    /// </summary>
    internal static FormDefinition RepeatingHiddenByOuterVisibilityDefinition { get; } = new()
    {
        Id = "form-repeating-hidden",
        Name = "Repeating hidden",
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
                            new FormNode { Id = "has-siblings", Type = NodeType.Boolean, Label = "Do you have siblings?" },
                            new FormNode
                            {
                                Id = "siblings",
                                Type = NodeType.Repeating,
                                Label = "Siblings",
                                ItemLabel = "Sibling",
                                MinRows = 0,
                                VisibleWhen = new ConditionGroup
                                {
                                    Conditions = [new Condition { Field = "has-siblings", Operator = ConditionOperator.IsTrue }],
                                },
                                Children = [new FormNode { Id = "sibling-name", Type = NodeType.Text, Label = "Sibling name" }],
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// A repeating group (<c>siblings</c>, <c>MinRows</c> 0) whose children are a text field
    /// (<c>sibling-name</c>) and a date field (<c>sibling-birthdate</c>) — for the draft
    /// save/resume round-trip test proving a row's <see cref="DateOnly"/> child rehydrates
    /// correctly.
    /// </summary>
    internal static FormDefinition RepeatingWithDateChildDefinition { get; } = new()
    {
        Id = "form-repeating-date",
        Name = "Repeating date",
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
                                Id = "siblings",
                                Type = NodeType.Repeating,
                                Label = "Siblings",
                                ItemLabel = "Sibling",
                                MinRows = 0,
                                Children =
                                [
                                    new FormNode { Id = "sibling-name", Type = NodeType.Text, Label = "Sibling name" },
                                    new FormNode { Id = "sibling-birthdate", Type = NodeType.Date, Label = "Sibling birthdate" },
                                ],
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// A top-level controller (<c>show-detail</c>) gates a top-level field (<c>outer-detail</c>);
    /// a repeating group (<c>items</c>, <c>MinRows</c> 0) has one required child
    /// (<c>confirm</c>) visible only while that SAME outer field, <c>outer-detail</c>, is
    /// non-blank. For the regression test proving a row child's own <c>VisibleWhen</c> resolves
    /// against the settled outer answers, not the raw ones — flipping <c>show-detail</c> back off
    /// must hide <c>confirm</c> too (agreeing with capture), never leave it stranded visible and
    /// required against a stale answer capture would already have dropped.
    /// </summary>
    internal static FormDefinition RepeatingChildVisibleWhenOuterFieldDefinition { get; } = new()
    {
        Id = "form-repeating-outer-visibility",
        Name = "Repeating outer visibility",
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
                            new FormNode { Id = "show-detail", Type = NodeType.Boolean, Label = "Show detail?" },
                            new FormNode
                            {
                                Id = "outer-detail",
                                Type = NodeType.Text,
                                Label = "Outer detail",
                                VisibleWhen = new ConditionGroup
                                {
                                    Conditions = [new Condition { Field = "show-detail", Operator = ConditionOperator.IsTrue }],
                                },
                            },
                            new FormNode
                            {
                                Id = "items",
                                Type = NodeType.Repeating,
                                Label = "Items",
                                ItemLabel = "Item",
                                MinRows = 0,
                                Children =
                                [
                                    new FormNode
                                    {
                                        Id = "confirm",
                                        Type = NodeType.Text,
                                        Label = "Confirm",
                                        Required = true,
                                        VisibleWhen = new ConditionGroup
                                        {
                                            Conditions = [new Condition { Field = "outer-detail", Operator = ConditionOperator.IsNotBlank }],
                                        },
                                    },
                                ],
                            },
                        ],
                    },
                ],
            },
        ],
    };

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
