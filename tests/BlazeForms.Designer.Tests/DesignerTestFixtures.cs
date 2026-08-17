using BlazeForms.Canvas;
using BlazeForms.Definitions;
using BlazeForms.Designer.Internal;
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

    /// <summary>
    /// Two pages: the first has one labelled field (so <c>DesignerCanvas</c> has a normal
    /// starting page), the second has one unlabelled email field -- an A11Y-01 blocking finding
    /// that anchors to a node on a page other than the one initially active, exercising
    /// <c>LinterDock</c>'s jump-to-node action switching pages as well as selection (Phase 7,
    /// PRD §8).
    /// </summary>
    internal static FormDefinition TwoPageBlockingIssueDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Two page form",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "First page",
                Sections = [new FormSection { Id = "section-1", Nodes = [new FormNode { Id = "node-a", Type = NodeType.Text, Label = "Field A" }] }],
            },
            new FormPage
            {
                Id = "page-2",
                Title = "Second page",
                Sections = [new FormSection { Id = "section-2", Nodes = [new FormNode { Id = "node-b", Type = NodeType.Email }] }],
            },
        ],
    };

    /// <summary>
    /// Two headings on one page, the second skipping from level 2 straight to level 4 -- the
    /// A11Y-08 advisory finding <c>LinterDock</c>'s one-click fix repairs (Phase 7, PRD §8).
    /// </summary>
    internal static FormDefinition HeadingSkipDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Heading skip form",
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
                            new FormNode { Id = "node-h1", Type = NodeType.Heading, Label = "Intro", Level = 2 },
                            new FormNode { Id = "node-h2", Type = NodeType.Heading, Label = "Skips a rung", Level = 4 },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// Three fields: <c>node-referenced</c> is named by another node's own
    /// <see cref="FormNode.VisibleWhen"/> (<c>node-dependent</c>'s) and by a single validation
    /// rule twice over -- once as its own <see cref="ValidationRule.Target"/>, once inside the
    /// rule's own <see cref="ValidationRule.Expression"/> -- so deleting it exercises every
    /// <see cref="Expressions.ReferenceKind"/> at once (Phase 7, PRD §4.1's delete-protection
    /// warning). <c>node-unreferenced</c> carries no reference at all, for the "deletes directly,
    /// no dialog" case.
    /// </summary>
    internal static FormDefinition ReferencedFieldDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Referenced field form",
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
                            new FormNode { Id = "node-referenced", Type = NodeType.Text, Label = "Referenced field" },
                            new FormNode
                            {
                                Id = "node-dependent",
                                Type = NodeType.Text,
                                Label = "Dependent field",
                                VisibleWhen = new ConditionGroup
                                {
                                    Conditions = [new Condition { Field = "node-referenced", Operator = ConditionOperator.IsNotBlank }],
                                },
                            },
                            new FormNode { Id = "node-unreferenced", Type = NodeType.Text, Label = "Unreferenced field" },
                        ],
                    },
                ],
            },
        ],
        ValidationRules =
        [
            new ValidationRule
            {
                Target = "node-referenced",
                Message = "Enter a value for 'Referenced field'.",
                Expression = new ConditionGroup
                {
                    Conditions = [new Condition { Field = "node-referenced", Operator = ConditionOperator.IsBlank }],
                },
            },
        ],
    };

    /// <summary>
    /// A number field ("node-referenced") named by a calc node's own ("node-calc") calculation --
    /// <c>DeleteProtectionDialog</c>'s own <see cref="ReferenceKind.Calculation"/> case
    /// (calc-engine-plan.md, Increment C).
    /// </summary>
    internal static FormDefinition CalcReferencedFieldDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Calc referenced field form",
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
                            new FormNode { Id = "node-referenced", Type = NodeType.Number, Label = "Referenced field" },
                            new FormNode
                            {
                                Id = "node-calc",
                                Type = NodeType.Calc,
                                Label = "Total",
                                Calculation = new CalcExpression
                                {
                                    Operation = CalcOperation.Sum,
                                    Operands = [new CalcOperand { Field = "node-referenced" }],
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
    /// Two number fields ("node-a", "node-b") and an uncalculated calc node ("node-calc") --
    /// <c>CalculationEditor</c>'s own candidate-field pool for the four numeric operations
    /// (calc-engine-plan.md, Increment C).
    /// </summary>
    internal static FormDefinition CalcNodeDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Calc node form",
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
                            new FormNode { Id = "node-a", Type = NodeType.Number, Label = "Field A" },
                            new FormNode { Id = "node-b", Type = NodeType.Number, Label = "Field B" },
                            new FormNode { Id = "node-calc", Type = NodeType.Calc, Label = "Total" },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// Same shape as <see cref="CalcNodeDefinition"/>, but "node-calc" already carries a Sum
    /// calculation over "node-a" alone -- seeds <c>CalculationEditorTests</c>' "existing
    /// calculation" cases.
    /// </summary>
    internal static FormDefinition CalcNodeWithExpressionDefinition(string formId)
    {
        var definition = CalcNodeDefinition(formId);
        return definition with
        {
            Pages =
            [
                definition.Pages[0] with
                {
                    Sections =
                    [
                        definition.Pages[0].Sections[0] with
                        {
                            Nodes =
                            [
                                definition.Pages[0].Sections[0].Nodes[0],
                                definition.Pages[0].Sections[0].Nodes[1],
                                definition.Pages[0].Sections[0].Nodes[2] with
                                {
                                    Calculation = new CalcExpression
                                    {
                                        Operation = CalcOperation.Sum,
                                        Operands = [new CalcOperand { Field = "node-a" }],
                                        Format = CalcFormat.Number,
                                    },
                                },
                            ],
                        },
                    ],
                },
            ],
        };
    }

    /// <summary>
    /// One page, one section holding a <see cref="NodeType.Repeating"/> group with two children
    /// ("child-a", "child-b") plus a sibling top-level field ("node-outside") -- the base fixture
    /// every group-scoping test (repeating-groups-plan.md, Increment C) starts from:
    /// <see cref="DefinitionMutations"/>'s child-aware mutations, <see cref="Canvas.DesignerCanvas"/>'s
    /// drill-in scope, and the boundary-aware rule editors all need at least one group with more
    /// than one child to exercise reordering, deletion neighbours, and sibling-vs-outside field
    /// picking.
    /// </summary>
    internal static FormDefinition RepeatingGroupDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Repeating group form",
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
                        Title = "Household",
                        Nodes =
                        [
                            new FormNode
                            {
                                Id = "group-1",
                                Type = NodeType.Repeating,
                                Label = "Household members",
                                ItemLabel = "Member",
                                MinRows = 0,
                                MaxRows = 5,
                                Children =
                                [
                                    new FormNode { Id = "child-a", Type = NodeType.Text, Label = "Full name" },
                                    new FormNode { Id = "child-b", Type = NodeType.Date, Label = "Date of birth" },
                                ],
                            },
                            new FormNode { Id = "node-outside", Type = NodeType.Text, Label = "Outside field" },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// The same shape as <see cref="TwoRepeatingGroupsDefinition"/>, but "group-1" also carries an
    /// uncalculated <see cref="NodeType.Calc"/> child ("calc-in-group") -- <c>CalculationEditor</c>'s
    /// own boundary-aware operand-field-picker fixture (repeating-groups-plan.md, Increment C):
    /// its candidate fields must offer "child-a2" (a numeric sibling) and "node-outside" (top-level)
    /// but never "child-c" (a different group's own child).
    /// </summary>
    internal static FormDefinition TwoRepeatingGroupsWithCalcDefinition(string formId)
    {
        var definition = TwoRepeatingGroupsDefinition(formId);
        return definition with
        {
            Pages =
            [
                definition.Pages[0] with
                {
                    Sections =
                    [
                        definition.Pages[0].Sections[0] with
                        {
                            Nodes =
                            [
                                definition.Pages[0].Sections[0].Nodes[0] with
                                {
                                    Children =
                                    [
                                        .. definition.Pages[0].Sections[0].Nodes[0].Children,
                                        new FormNode { Id = "calc-in-group", Type = NodeType.Calc, Label = "Calc in group" },
                                    ],
                                },
                                definition.Pages[0].Sections[0].Nodes[1],
                                definition.Pages[0].Sections[0].Nodes[2],
                            ],
                        },
                    ],
                },
            ],
        };
    }

    /// <summary>
    /// The same shape as <see cref="RepeatingGroupDefinition"/>, but "child-a" is named by
    /// "child-b"'s own <see cref="FormNode.VisibleWhen"/> -- a sibling-within-the-same-group
    /// reference, legal under FR-04 -- so deleting the whole group (<c>group-1</c>) exercises
    /// <c>DeleteProtectionDialog</c>'s own descendant aggregation (repeating-groups-plan.md,
    /// Increment C): the warning must name that reference even though it names neither the
    /// group's own id nor "child-a" directly by a top-level rule.
    /// </summary>
    internal static FormDefinition RepeatingGroupWithReferencedChildDefinition(string formId)
    {
        var definition = RepeatingGroupDefinition(formId);
        return definition with
        {
            Pages =
            [
                definition.Pages[0] with
                {
                    Sections =
                    [
                        definition.Pages[0].Sections[0] with
                        {
                            Nodes =
                            [
                                definition.Pages[0].Sections[0].Nodes[0] with
                                {
                                    Children =
                                    [
                                        definition.Pages[0].Sections[0].Nodes[0].Children[0],
                                        definition.Pages[0].Sections[0].Nodes[0].Children[1] with
                                        {
                                            VisibleWhen = new ConditionGroup
                                            {
                                                Conditions = [new Condition { Field = "child-a", Operator = ConditionOperator.IsNotBlank }],
                                            },
                                        },
                                    ],
                                },
                                definition.Pages[0].Sections[0].Nodes[1],
                            ],
                        },
                    ],
                },
            ],
        };
    }

    /// <summary>
    /// Two repeating groups ("group-1" with children "child-a"/"child-a2", "group-2" with child
    /// "child-c") plus a top-level field ("node-outside") -- the boundary-aware field pickers' own
    /// fixture (repeating-groups-plan.md, Increment C): editing "child-a" must offer its sibling
    /// "child-a2" and "node-outside", but never "child-c" (a different group's child); editing
    /// "node-outside" must exclude every child of either group.
    /// </summary>
    internal static FormDefinition TwoRepeatingGroupsDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Two repeating groups form",
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
                                Id = "group-1",
                                Type = NodeType.Repeating,
                                Label = "Group one",
                                Children =
                                [
                                    new FormNode { Id = "child-a", Type = NodeType.Text, Label = "Child A" },
                                    new FormNode { Id = "child-a2", Type = NodeType.Number, Label = "Child A2" },
                                ],
                            },
                            new FormNode
                            {
                                Id = "group-2",
                                Type = NodeType.Repeating,
                                Label = "Group two",
                                Children = [new FormNode { Id = "child-c", Type = NodeType.Number, Label = "Child C" }],
                            },
                            new FormNode { Id = "node-outside", Type = NodeType.Number, Label = "Outside field" },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// The same shape as <see cref="RepeatingGroupDefinition"/>, but the group has no children at
    /// all yet -- <see cref="Canvas.DesignerCanvas"/>'s own empty-scope state (repeating-groups-plan.md,
    /// Increment C).
    /// </summary>
    internal static FormDefinition EmptyRepeatingGroupDefinition(string formId)
    {
        var definition = RepeatingGroupDefinition(formId);
        return definition with
        {
            Pages =
            [
                definition.Pages[0] with
                {
                    Sections =
                    [
                        definition.Pages[0].Sections[0] with
                        {
                            Nodes =
                            [
                                definition.Pages[0].Sections[0].Nodes[0] with { Children = [] },
                                definition.Pages[0].Sections[0].Nodes[1],
                            ],
                        },
                    ],
                },
            ],
        };
    }

    /// <summary>
    /// Two calc nodes: "calc-b" already carries a calculation that reads "calc-a" -- giving
    /// "calc-a" a calculation that reads "calc-b" would close calc-a -&gt; calc-b -&gt; calc-a,
    /// <c>CalculationEditor</c>'s own cycle-rejection case, on the calculation graph rather than
    /// the visibility one.
    /// </summary>
    internal static FormDefinition TwoCalcNodesDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Two calc nodes form",
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
                            new FormNode { Id = "calc-a", Type = NodeType.Calc, Label = "Calc A" },
                            new FormNode
                            {
                                Id = "calc-b",
                                Type = NodeType.Calc,
                                Label = "Calc B",
                                Calculation = new CalcExpression
                                {
                                    Operation = CalcOperation.Sum,
                                    Operands = [new CalcOperand { Field = "calc-a" }],
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
    /// Two pages: page-1 holds a repeating group ("group-1", children "child-a"/"child-b") plus a
    /// top-level field ("solo-1") in "section-1"; page-2 holds a single top-level field ("solo-2")
    /// in "section-2". The regression fixture for the stale-scope palette-routing guard
    /// (repeating-groups-plan.md, Increment C): scoping into "group-1" on page-1 and then switching
    /// to page-2 must add to page-2's own section, never into the page-1 group left behind.
    /// </summary>
    internal static FormDefinition TwoPageRepeatingGroupDefinition(string formId) => new()
    {
        Id = formId,
        Name = "Two page repeating group form",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "Household page",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-1",
                        Title = "Household",
                        Nodes =
                        [
                            new FormNode
                            {
                                Id = "group-1",
                                Type = NodeType.Repeating,
                                Label = "Household members",
                                ItemLabel = "Member",
                                MinRows = 0,
                                MaxRows = 5,
                                Children =
                                [
                                    new FormNode { Id = "child-a", Type = NodeType.Text, Label = "Full name" },
                                    new FormNode { Id = "child-b", Type = NodeType.Date, Label = "Date of birth" },
                                ],
                            },
                            new FormNode { Id = "solo-1", Type = NodeType.Text, Label = "Page one field" },
                        ],
                    },
                ],
            },
            new FormPage
            {
                Id = "page-2",
                Title = "Coverage page",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-2",
                        Title = "Coverage",
                        Nodes =
                        [
                            new FormNode { Id = "solo-2", Type = NodeType.Text, Label = "Page two field" },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// The same shape as <see cref="RepeatingGroupDefinition"/>, but "group-1" gains a third child
    /// ("child-c") whose own <see cref="FormNode.VisibleWhen"/> names BOTH "child-a" and "child-b"
    /// in one rule -- a single referencing site that points at two members of the same group. The
    /// regression fixture for the delete-protection dedup guard (repeating-groups-plan.md, Increment
    /// C): deleting the whole group must list "child-c"'s reference exactly once, not once per
    /// member it happens to name.
    /// </summary>
    internal static FormDefinition RepeatingGroupWithRuleReferencingTwoChildrenDefinition(string formId)
    {
        var definition = RepeatingGroupDefinition(formId);
        return definition with
        {
            Pages =
            [
                definition.Pages[0] with
                {
                    Sections =
                    [
                        definition.Pages[0].Sections[0] with
                        {
                            Nodes =
                            [
                                definition.Pages[0].Sections[0].Nodes[0] with
                                {
                                    Children =
                                    [
                                        definition.Pages[0].Sections[0].Nodes[0].Children[0],
                                        definition.Pages[0].Sections[0].Nodes[0].Children[1],
                                        new FormNode
                                        {
                                            Id = "child-c",
                                            Type = NodeType.Text,
                                            Label = "Notes",
                                            VisibleWhen = new ConditionGroup
                                            {
                                                Join = ConditionJoin.All,
                                                Conditions =
                                                [
                                                    new Condition { Field = "child-a", Operator = ConditionOperator.IsNotBlank },
                                                    new Condition { Field = "child-b", Operator = ConditionOperator.IsNotBlank },
                                                ],
                                            },
                                        },
                                    ],
                                },
                                definition.Pages[0].Sections[0].Nodes[1],
                            ],
                        },
                    ],
                },
            ],
        };
    }
}
