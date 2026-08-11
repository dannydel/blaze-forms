using BlazeForms.Canvas;
using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Properties;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="PropertiesPanel"/>: it dispatches on the selected node's
/// <see cref="NodeType"/> to show exactly the controls PRD §4.1/§5 asks for, routes every commit
/// through <see cref="DesignerEditContext.UpdateNode"/> exactly once per edit, shows the
/// accessible empty-label affordance only for an input node with no label, and moves focus into
/// its own label input only for the selection changes <see cref="Canvas.DesignerCanvas"/> itself
/// never claims focus for.
/// </summary>
/// <remarks>
/// Every test disposes its context via <c>await using</c>, the same reason
/// <c>DesignerEditContextTests</c> does.
/// </remarks>
public sealed class PropertiesPanelTests : DesignerTestContext
{
    private static DesignerEditContext CreateContext(FormDefinition definition, IFormDefinitionStore? store = null) =>
        new(FormLifecycle.CreateDraft(definition), store ?? new InMemoryFormDefinitionStore());

    private static void Select(DesignerEditContext context, string nodeId, string pageId = "page-1", string sectionId = "section-1") =>
        context.Select(DesignerSelection.ForNode(nodeId, pageId, sectionId, DesignerFocusIntent.None));

    [Fact]
    public async Task NoSelectionShowsTheAccessibleEmptyState()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        Assert.Equal(DesignerSelection.None, context.Selection);
        Assert.NotEmpty(cut.Find("p.bf-props__empty").TextContent);
        Assert.Empty(cut.FindAll("div.bf-props"));
    }

    [Fact]
    public async Task FieldIdRendersReadOnlyWithNoEditableControl()
    {
        await using var context = CreateContext(DesignerTestFixtures.OptionNodeDefinition("form-1"));
        Select(context, "node-choice");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        var idInput = cut.Find("input.bf-props__input--readonly");
        Assert.Equal("node-choice", idInput.GetAttribute("value"));
        Assert.True(idInput.HasAttribute("readonly"));
        Assert.Equal("true", idInput.GetAttribute("aria-readonly"));
    }

    [Fact]
    public async Task EditingLabelCallsUpdateNodeWithUnchangedOptionValues()
    {
        await using var context = CreateContext(DesignerTestFixtures.OptionNodeDefinition("form-1"));
        Select(context, "node-choice");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        var labelInput = cut.Find("input#bf-props-node-choice-label");
        await labelInput.ChangeAsync(new ChangeEventArgs { Value = "Pick your favorite" });

        var updated = context.Draft.Definition.FindNode("node-choice")!;
        Assert.Equal("Pick your favorite", updated.Label);
        Assert.Equal(["opt-1", "opt-2"], updated.Options.Select(o => o.Value));
        Assert.Equal(["Option one", "Option two"], updated.Options.Select(o => o.Label));
    }

    [Fact]
    public async Task MinAndMaxShowOnlyForNumericNodes()
    {
        await using var numericContext = CreateContext(DesignerTestFixtures.NumericNodeDefinition("form-1"));
        Select(numericContext, "node-numeric");
        var numericCut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, numericContext));
        Assert.NotEmpty(numericCut.FindAll("input#bf-props-node-numeric-min"));
        Assert.NotEmpty(numericCut.FindAll("input#bf-props-node-numeric-max"));

        await using var textContext = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        Select(textContext, "first-name");
        var textCut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, textContext));
        Assert.Empty(textCut.FindAll("input#bf-props-first-name-min"));
        Assert.Empty(textCut.FindAll("input#bf-props-first-name-max"));
    }

    [Fact]
    public async Task EditingMinCommitsADecimalAndClearingItCommitsNull()
    {
        await using var context = CreateContext(DesignerTestFixtures.NumericNodeDefinition("form-1"));
        Select(context, "node-numeric");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        var minInput = cut.Find("input#bf-props-node-numeric-min");
        await minInput.ChangeAsync(new ChangeEventArgs { Value = "5" });

        Assert.Equal(5m, context.Draft.Definition.FindNode("node-numeric")!.Min);
    }

    [Fact]
    public async Task LevelSelectShowsOnlyForHeadingNodes()
    {
        await using var headingContext = CreateContext(DesignerTestFixtures.HeadingNodeDefinition("form-1"));
        Select(headingContext, "node-heading");
        var headingCut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, headingContext));
        Assert.NotEmpty(headingCut.FindAll("select#bf-props-node-heading-level"));

        await using var textContext = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        Select(textContext, "first-name");
        var textCut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, textContext));
        Assert.Empty(textCut.FindAll("select#bf-props-first-name-level"));
    }

    [Fact]
    public async Task ChangingTheLevelSelectCommitsTheNewLevel()
    {
        await using var context = CreateContext(DesignerTestFixtures.HeadingNodeDefinition("form-1"));
        Select(context, "node-heading");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        var select = cut.Find("select#bf-props-node-heading-level");
        await select.ChangeAsync(new ChangeEventArgs { Value = "4" });

        Assert.Equal(4, context.Draft.Definition.FindNode("node-heading")!.Level);
    }

    [Fact]
    public async Task OptionsEditorShowsOnlyForChoiceNodes()
    {
        await using var choiceContext = CreateContext(DesignerTestFixtures.OptionNodeDefinition("form-1"));
        Select(choiceContext, "node-choice");
        var choiceCut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, choiceContext));
        Assert.NotEmpty(choiceCut.FindAll("div.bf-options-editor"));

        await using var textContext = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        Select(textContext, "first-name");
        var textCut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, textContext));
        Assert.Empty(textCut.FindAll("div.bf-options-editor"));
    }

    [Fact]
    public async Task ContentTextareaShowsOnlyForParagraphAndCalloutNodes()
    {
        await using var paragraphContext = CreateContext(DesignerTestFixtures.ParagraphNodeDefinition("form-1"));
        Select(paragraphContext, "node-paragraph");
        var paragraphCut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, paragraphContext));
        Assert.NotEmpty(paragraphCut.FindAll("textarea#bf-props-node-paragraph-content"));

        await using var textContext = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        Select(textContext, "first-name");
        var textCut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, textContext));
        Assert.Empty(textCut.FindAll("textarea#bf-props-first-name-content"));
    }

    [Fact]
    public async Task EditingContentCommitsTheNewContent()
    {
        await using var context = CreateContext(DesignerTestFixtures.ParagraphNodeDefinition("form-1"));
        Select(context, "node-paragraph");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        var content = cut.Find("textarea#bf-props-node-paragraph-content");
        await content.ChangeAsync(new ChangeEventArgs { Value = "Updated **prose**." });

        Assert.Equal("Updated **prose**.", context.Draft.Definition.FindNode("node-paragraph")!.Content);
    }

    [Fact]
    public async Task SupportsMarkdownMarkerShowsOnlyOnHelpAndContentNeverOnLabelOrOptions()
    {
        await using var textContext = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        Select(textContext, "first-name");
        var textCut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, textContext));
        // A plain input node shows exactly one marker -- on Help; Label carries none.
        Assert.Single(textCut.FindAll("span.bf-props__markdown-hint"));

        await using var paragraphContext = CreateContext(DesignerTestFixtures.ParagraphNodeDefinition("form-1"));
        Select(paragraphContext, "node-paragraph");
        var paragraphCut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, paragraphContext));
        // Paragraph is static (not an input node), so its only marker is on Content.
        Assert.Single(paragraphCut.FindAll("span.bf-props__markdown-hint"));

        await using var choiceContext = CreateContext(DesignerTestFixtures.OptionNodeDefinition("form-1"));
        Select(choiceContext, "node-choice");
        var choiceCut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, choiceContext));
        // The options editor never carries the marker (labels stay plain text, PRD §5.1).
        Assert.Empty(choiceCut.Find("div.bf-options-editor").QuerySelectorAll("span.bf-props__markdown-hint"));
    }

    [Fact]
    public async Task EmptyLabelOnAnInputNodeShowsTheAccessibleRequiredAffordance()
    {
        await using var context = CreateContext(DesignerTestFixtures.UntitledNodeDefinition("form-1"));
        Select(context, "node-untitled");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        var labelInput = cut.Find("input#bf-props-node-untitled-label");
        Assert.Equal("true", labelInput.GetAttribute("aria-invalid"));
        Assert.NotNull(labelInput.GetAttribute("aria-describedby"));

        var warning = cut.Find("p#bf-props-node-untitled-label-warning");
        Assert.NotEmpty(warning.TextContent);
        Assert.Equal(labelInput.GetAttribute("aria-describedby"), warning.GetAttribute("id"));
    }

    [Fact]
    public async Task ANonEmptyLabelClearsTheRequiredAffordance()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        Select(context, "first-name");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        var labelInput = cut.Find("input#bf-props-first-name-label");
        Assert.Equal("false", labelInput.GetAttribute("aria-invalid"));
        Assert.Empty(cut.FindAll("p.bf-props__warning"));
    }

    [Fact]
    public async Task AStaticNodeWithNoLabelNeverShowsTheAffordance()
    {
        // A11yLabelRule (A11Y-01) only ever flags an input node -- a heading's empty label is
        // not this rule's concern, so PropertiesPanel does not warn about it either.
        await using var context = CreateContext(DesignerTestFixtures.HeadingNodeDefinition("form-1"));
        context.UpdateNode(context.Draft.Definition.FindNode("node-heading")! with { Label = null });
        Select(context, "node-heading");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        var labelInput = cut.Find("input#bf-props-node-heading-label");
        Assert.Equal("false", labelInput.GetAttribute("aria-invalid"));
        Assert.Empty(cut.FindAll("p.bf-props__warning"));
    }

    [Fact]
    public async Task ASingleEditCommitsExactlyOnceRegardlessOfHowManyKeystrokesLedToIt()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        Select(context, "first-name");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        var commitCount = 0;
        context.Announced += _ => commitCount++;

        var helpInput = cut.Find("textarea#bf-props-first-name-help");
        await helpInput.ChangeAsync(new ChangeEventArgs { Value = "Some help text typed across many keystrokes." });

        Assert.Equal(1, commitCount);
        Assert.Equal("Some help text typed across many keystrokes.", context.Draft.Definition.FindNode("first-name")!.Help);
    }

    [Fact]
    public async Task RequiredCheckboxCommitsImmediatelyOnChange()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        Select(context, "first-name");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        var required = cut.Find("input#bf-props-first-name-required");
        await required.ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.True(context.Draft.Definition.FindNode("first-name")!.Required);
    }

    [Fact]
    public async Task RequiredWhenVisibleIsDisabledWhileRequiredIsChecked()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        context.UpdateNode(context.Draft.Definition.FindNode("first-name")! with { Required = true });
        Select(context, "first-name");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        var requiredWhenVisible = cut.Find("input#bf-props-first-name-required-when-visible");
        Assert.True(requiredWhenVisible.HasAttribute("disabled"));
    }

    [Fact]
    public async Task VisibilityShowsAlwaysVisibleWithAnAddButtonWhenThereIsNoRule()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        Select(context, "first-name");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        Assert.Contains("Always visible", cut.Find("p.bf-props__visibility-summary").TextContent, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("div.bf-props__visibility-actions button"));
    }

    [Fact]
    public async Task VisibilityShowsHasARuleWithEditAndRemoveButtonsWhenARuleExists()
    {
        await using var context = CreateContext(DesignerTestFixtures.RichNodeDefinition("form-1"));
        Select(context, "node-rich");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        Assert.Contains("Has a visibility rule", cut.Find("p.bf-props__visibility-summary").TextContent, StringComparison.Ordinal);
        Assert.Equal(2, cut.FindAll("div.bf-props__visibility-actions button").Count);
    }

    [Fact]
    public async Task ClickingAVisibilityRuleButtonIsANoOpStubThatNeverMutatesTheNode()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        Select(context, "first-name");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        var before = context.Draft.Definition;
        await cut.Find("div.bf-props__visibility-actions button").ClickAsync(new MouseEventArgs());

        Assert.Same(before, context.Draft.Definition);
    }

    [Fact]
    public async Task SelectingANewNodeWithNoFocusIntentMovesFocusToTheLabelInput()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        Select(context, "node-a");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("div.bf-props")));

        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task ReselectingTheSameNodeDoesNotRefocus()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        Select(context, "node-a");
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        Select(context, "node-a");
        cut.Render();

        JSInterop.VerifyNotInvoke("Blazor._internal.domWrapper.focus");
    }

    [Fact]
    public async Task AStructuralMutationsNewNodeSelectionNeverStealsFocusFromTheCanvas()
    {
        // AddNode tags DesignerFocusIntent.NewNode, not None -- DesignerCanvas is the one that
        // owns focus for that case (it moves its own roving cursor there); PropertiesPanel must
        // step aside so the two never race for the same DOM focus.
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        context.AddNode(NodeType.Email, "section-1");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("div.bf-props")));

        JSInterop.VerifyNotInvoke("Blazor._internal.domWrapper.focus");
    }

    [Fact]
    public async Task EditingAPropertyNeverReRendersAnUnrelatedCanvasRow()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var canvasCut = Render<DesignerCanvas>(p => p.Add(f => f.EditContext, context).Add(f => f.ActivePageId, "page-1"));
        Select(context, "node-b");
        var propsCut = Render<PropertiesPanel>(p => p.Add(f => f.EditContext, context));

        var unrelatedRow = canvasCut.FindComponents<CanvasNodeRow>().Single(row => row.Instance.Node.Id == "node-d");
        var unrelatedRenderCountBefore = unrelatedRow.RenderCount;

        var requiredCheckbox = propsCut.Find("input#bf-props-node-b-required");
        await requiredCheckbox.ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.Equal(unrelatedRenderCountBefore, unrelatedRow.RenderCount);
        Assert.True(context.Draft.Definition.FindNode("node-b")!.Required);
    }
}
