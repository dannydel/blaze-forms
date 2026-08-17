using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Rules;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="VisibilityRuleEditor"/>: it seeds its working state from the node's current
/// rule (or the absence of one), the All/Any toggle and condition rows round-trip into a
/// well-formed <see cref="ConditionGroup"/>, a candidate that would close a visibility cycle is
/// REJECTED without ever touching the draft and renders the named path in a <c>role="alert"</c>
/// with focus moved there, and Esc/Cancel close without mutating (PRD §4.1, §6, §11).
/// </summary>
public sealed class VisibilityRuleEditorTests : DesignerTestContext
{
    private static DesignerEditContext CreateContext(FormDefinition definition, IFormDefinitionStore? store = null) =>
        new(FormLifecycle.CreateDraft(definition), store ?? new InMemoryFormDefinitionStore());

    [Fact]
    public async Task RendersAsAFocusLabelledModalDialog()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-a"));

        var dialog = cut.Find("div.bf-visibility-dialog");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        var labelledBy = dialog.GetAttribute("aria-labelledby");
        Assert.NotNull(labelledBy);
        Assert.Equal(cut.Find("h2").Id, labelledBy);
    }

    [Fact]
    public async Task SeedsFromANodeWithNoExistingRule()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-a"));

        Assert.Empty(cut.FindAll("div.bf-condition-row"));
        var allRadio = (AngleSharp.Html.Dom.IHtmlInputElement)cut.FindAll("input[type='radio']")[0];
        Assert.True(allRadio.IsChecked);
    }

    [Fact]
    public async Task SeedsFromANodesExistingRule()
    {
        await using var context = CreateContext(DesignerTestFixtures.RichNodeDefinition("form-1"));
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-rich"));

        Assert.Single(cut.FindAll("div.bf-condition-row"));
    }

    [Fact]
    public async Task AddingAConditionAndApplyingBuildsAWellFormedConditionGroup()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-a"));

        await cut.Find("button.bf-visibility-dialog__add-button").ClickAsync(new MouseEventArgs());
        var row = cut.Find("div.bf-condition-row");
        await row.QuerySelector("select")!.ChangeAsync("node-b");
        await row.QuerySelectorAll("select")[1].ChangeAsync(nameof(ConditionOperator.Is));
        await row.QuerySelector("input[type='text']")!.ChangeAsync("Active");

        await cut.Find("button.bf-visibility-dialog__button--primary").ClickAsync(new MouseEventArgs());

        var rule = context.Draft.Definition.FindNode("node-a")!.VisibleWhen;
        Assert.NotNull(rule);
        Assert.Equal(ConditionJoin.All, rule.Join);
        var condition = Assert.Single(rule.Conditions);
        Assert.Equal("node-b", condition.Field);
        Assert.Equal(ConditionOperator.Is, condition.Operator);
        Assert.Equal("Active", condition.Value);
    }

    /// <summary>
    /// The authoring-time counterpart to the linter's blocking FR-04 rule (repeating-groups-plan.md,
    /// Increment C): editing a repeating group's own child offers its own sibling and every
    /// top-level field, but never a different group's own child.
    /// </summary>
    [Fact]
    public async Task FieldPickerForAGroupsChildOffersSiblingsAndTopLevelFieldsButNotAnotherGroupsChildren()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoRepeatingGroupsDefinition("form-1"));
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "child-a"));

        await cut.Find("button.bf-visibility-dialog__add-button").ClickAsync(new MouseEventArgs());
        var options = cut.Find("div.bf-condition-row").QuerySelector("select")!
            .QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToArray();

        Assert.Contains("child-a2", options);
        Assert.Contains("node-outside", options);
        Assert.DoesNotContain("child-c", options);
    }

    /// <summary>
    /// The reverse boundary (repeating-groups-plan.md, Increment C): editing a top-level field
    /// excludes every repeating group's own children entirely.
    /// </summary>
    [Fact]
    public async Task FieldPickerForATopLevelNodeExcludesEveryGroupsChildren()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoRepeatingGroupsDefinition("form-1"));
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-outside"));

        await cut.Find("button.bf-visibility-dialog__add-button").ClickAsync(new MouseEventArgs());
        var options = cut.Find("div.bf-condition-row").QuerySelector("select")!
            .QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToArray();

        Assert.DoesNotContain("child-a", options);
        Assert.DoesNotContain("child-a2", options);
        Assert.DoesNotContain("child-c", options);
    }

    [Fact]
    public async Task SwitchingToAnyRoundTripsTheJoin()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-a"));

        await cut.Find("button.bf-visibility-dialog__add-button").ClickAsync(new MouseEventArgs());

        // The fresh row defaults to the first field in the list, which is this very node
        // ("node-a") -- point it at a different field first, or Apply would reject its own
        // candidate as the self-reference cycle it actually is.
        await cut.Find("div.bf-condition-row select").ChangeAsync("node-b");

        var anyRadio = cut.FindAll("input[type='radio']")[1];
        await anyRadio.ChangeAsync(new ChangeEventArgs { Value = true });

        await cut.Find("button.bf-visibility-dialog__button--primary").ClickAsync(new MouseEventArgs());

        Assert.Equal(ConditionJoin.Any, context.Draft.Definition.FindNode("node-a")!.VisibleWhen!.Join);
    }

    [Fact]
    public async Task RemovingTheLastConditionAndApplyingClearsTheRule()
    {
        await using var context = CreateContext(DesignerTestFixtures.RichNodeDefinition("form-1"));
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-rich"));

        await cut.Find("div.bf-condition-row button.bf-condition-row__remove-button").ClickAsync(new MouseEventArgs());
        await cut.Find("button.bf-visibility-dialog__button--primary").ClickAsync(new MouseEventArgs());

        Assert.Null(context.Draft.Definition.FindNode("node-rich")!.VisibleWhen);
    }

    [Fact]
    public async Task ApplyingRaisesOnClosed()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var closed = false;
        var cut = Render<VisibilityRuleEditor>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.NodeId, "node-a")
            .Add(d => d.OnClosed, () => closed = true));

        await cut.Find("button.bf-visibility-dialog__button--primary").ClickAsync(new MouseEventArgs());

        Assert.True(closed);
    }

    [Fact]
    public async Task EscCancelsWithoutTouchingTheDraftAndRaisesOnClosed()
    {
        await using var context = CreateContext(DesignerTestFixtures.RichNodeDefinition("form-1"));
        var closed = false;
        var cut = Render<VisibilityRuleEditor>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.NodeId, "node-rich")
            .Add(d => d.OnClosed, () => closed = true));

        var before = context.Draft.Definition;
        await cut.Find("div.bf-visibility-dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Same(before, context.Draft.Definition);
        Assert.True(closed);
    }

    [Fact]
    public async Task CancelButtonCancelsWithoutTouchingTheDraft()
    {
        await using var context = CreateContext(DesignerTestFixtures.RichNodeDefinition("form-1"));
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-rich"));

        var before = context.Draft.Definition;
        await cut.Find("button.bf-visibility-dialog__button:not(.bf-visibility-dialog__button--primary)").ClickAsync(new MouseEventArgs());

        Assert.Same(before, context.Draft.Definition);
    }

    /// <summary>
    /// A candidate that would create a direct two-node cycle is REJECTED: the node is NOT
    /// mutated, the dialog stays open, the named path renders in a <c>role="alert"</c>, and focus
    /// moves there.
    /// </summary>
    [Fact]
    public async Task ACycleCreatingCandidateIsRejectedWithTheNamedPathAndFocusMovesToTheAlert()
    {
        // node-b already depends on node-a (TwoSectionDefinition has no rules of its own, so seed
        // one directly): giving node-a a rule that depends on node-b would close a → b → a.
        var definition = DesignerTestFixtures.TwoSectionDefinition("form-1");
        var withDependency = definition with
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
                                definition.Pages[0].Sections[0].Nodes[1] with
                                {
                                    VisibleWhen = new ConditionGroup
                                    {
                                        Conditions = [new Condition { Field = "node-a", Operator = ConditionOperator.IsNotBlank }],
                                    },
                                },
                                definition.Pages[0].Sections[0].Nodes[2],
                            ],
                        },
                        definition.Pages[0].Sections[1],
                    ],
                },
            ],
        };

        await using var context = CreateContext(withDependency);
        var before = context.Draft.Definition;
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-a"));

        await cut.Find("button.bf-visibility-dialog__add-button").ClickAsync(new MouseEventArgs());
        var row = cut.Find("div.bf-condition-row");
        await row.QuerySelector("select")!.ChangeAsync("node-b");
        await row.QuerySelectorAll("select")[1].ChangeAsync(nameof(ConditionOperator.IsNotBlank));

        await cut.Find("button.bf-visibility-dialog__button--primary").ClickAsync(new MouseEventArgs());

        var alert = cut.Find("div[role='alert']");
        Assert.Contains("Field A", alert.TextContent, StringComparison.Ordinal);
        Assert.Contains("Field B", alert.TextContent, StringComparison.Ordinal);
        Assert.Same(before, context.Draft.Definition);

        // Once for the dialog's own first-render focus (the "All" radio), once more for the
        // freshly added row's own Field select (PRD §11, WCAG 2.4.3), and once more for the
        // rejection moving focus to the alert.
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 3);
    }

    // --- Focus management on add/remove (PRD §11, WCAG 2.4.3) --------------------------------

    [Fact]
    public async Task AddingAConditionFocusesTheNewRowsFieldSelect()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-a"));

        await cut.Find("button.bf-visibility-dialog__add-button").ClickAsync(new MouseEventArgs());

        Assert.Single(cut.FindAll("div.bf-condition-row"));

        // Once for the dialog's own first-render focus (the "All" radio), once more for the
        // freshly added row's own Field select.
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 2);
    }

    /// <summary>
    /// Builds a definition where node-a already carries a three-condition rule against node-b --
    /// a field with no rule of its own, so extending or shrinking node-a's own rule can never
    /// close a cycle.
    /// </summary>
    private static FormDefinition ThreeConditionRuleAgainstNodeB()
    {
        var definition = DesignerTestFixtures.TwoSectionDefinition("form-1");
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
                                    VisibleWhen = new ConditionGroup
                                    {
                                        Conditions =
                                        [
                                            new Condition { Field = "node-b", Operator = ConditionOperator.IsNotBlank },
                                            new Condition { Field = "node-b", Operator = ConditionOperator.IsBlank },
                                            new Condition { Field = "node-b", Operator = ConditionOperator.IsTrue },
                                        ],
                                    },
                                },
                                definition.Pages[0].Sections[0].Nodes[1],
                                definition.Pages[0].Sections[0].Nodes[2],
                            ],
                        },
                        definition.Pages[0].Sections[1],
                    ],
                },
            ],
        };
    }

    [Fact]
    public async Task RemovingAMiddleConditionFocusesThePreviousRowsFieldSelect()
    {
        await using var context = CreateContext(ThreeConditionRuleAgainstNodeB());
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-a"));

        var rows = cut.FindAll("div.bf-condition-row");
        Assert.Equal(3, rows.Count);

        await rows[1].QuerySelector("button.bf-condition-row__remove-button")!.ClickAsync(new MouseEventArgs());

        Assert.Equal(2, cut.FindAll("div.bf-condition-row").Count);

        // Once for the dialog's own first-render focus, once more for the previous row's Field
        // select.
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 2);
    }

    [Fact]
    public async Task RemovingTheFirstConditionFocusesTheNewFirstRowsFieldSelect()
    {
        await using var context = CreateContext(ThreeConditionRuleAgainstNodeB());
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-a"));

        var rows = cut.FindAll("div.bf-condition-row");
        Assert.Equal(3, rows.Count);

        await rows[0].QuerySelector("button.bf-condition-row__remove-button")!.ClickAsync(new MouseEventArgs());

        Assert.Equal(2, cut.FindAll("div.bf-condition-row").Count);

        // Once for the dialog's own first-render focus, once more for the row that slides into
        // the first position.
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 2);
    }

    [Fact]
    public async Task RemovingTheLastRemainingConditionFocusesTheAddConditionButton()
    {
        await using var context = CreateContext(DesignerTestFixtures.RichNodeDefinition("form-1"));
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-rich"));

        await cut.Find("div.bf-condition-row button.bf-condition-row__remove-button").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("div.bf-condition-row"));

        // Once for the dialog's own first-render focus, once more for the add-condition button.
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 2);
    }

    [Fact]
    public async Task TheFocusTrapModuleIsImportedAndDisposed()
    {
        var module = JSInterop.SetupModule(VisibilityRuleEditor.ModulePath);
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<VisibilityRuleEditor>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-a"));

        cut.WaitForAssertion(() => Assert.True(cut.Instance.HasImportedModule));
        module.VerifyInvoke("attachFocusTrap");

        await cut.Instance.DisposeAsync();

        Assert.False(cut.Instance.HasImportedModule);
    }
}
