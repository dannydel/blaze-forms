using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Rules;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="ValidationRuleEditor"/>: it renders one block per
/// <see cref="FormDefinition.ValidationRules"/> entry with a Target select, a Message textarea, and
/// the same All/Any-plus-<see cref="ConditionRow"/> UI <see cref="VisibilityRuleEditor"/> uses for
/// its own expression, and every add/remove/edit action writes the complete replacement list
/// through <see cref="DesignerEditContext.SetValidationRules"/> (PRD §4.1, §6).
/// </summary>
public sealed class ValidationRuleEditorTests : DesignerTestContext
{
    private static DesignerEditContext CreateContext(FormDefinition definition, IFormDefinitionStore? store = null) =>
        new(FormLifecycle.CreateDraft(definition), store ?? new InMemoryFormDefinitionStore());

    [Fact]
    public async Task ShowsTheEmptyStateWhenThereAreNoRules()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<ValidationRuleEditor>(p => p.Add(d => d.EditContext, context));

        Assert.NotEmpty(cut.Find("p.bf-validation-rules__empty").TextContent);
        Assert.Empty(cut.FindAll("div.bf-validation-rules__rule"));
    }

    [Fact]
    public async Task AddingARuleWritesADefaultRuleThroughSetValidationRules()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<ValidationRuleEditor>(p => p.Add(d => d.EditContext, context));

        await cut.Find("button.bf-validation-rules__add-button").ClickAsync(new MouseEventArgs());

        var rule = Assert.Single(context.Draft.Definition.ValidationRules);
        Assert.Equal("node-a", rule.Target);
        Assert.Equal(string.Empty, rule.Message);
        Assert.Empty(rule.Expression.Conditions);
    }

    [Fact]
    public async Task EditingTargetMessageAndConditionsCommitsEachAsOneWrite()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<ValidationRuleEditor>(p => p.Add(d => d.EditContext, context));

        await cut.Find("button.bf-validation-rules__add-button").ClickAsync(new MouseEventArgs());

        await cut.Find("select").ChangeAsync("node-b");
        Assert.Equal("node-b", context.Draft.Definition.ValidationRules[0].Target);

        await cut.Find("textarea").ChangeAsync(new ChangeEventArgs { Value = "Enter a value for 'Field B'." });
        Assert.Equal("Enter a value for 'Field B'.", context.Draft.Definition.ValidationRules[0].Message);

        await cut.Find("button.bf-validation-rules__button:not(.bf-validation-rules__remove-button)").ClickAsync(new MouseEventArgs());
        var condition = Assert.Single(context.Draft.Definition.ValidationRules[0].Expression.Conditions);
        Assert.Equal("node-a", condition.Field);

        await cut.Find("div.bf-condition-row select").ChangeAsync("node-c");
        Assert.Equal("node-c", context.Draft.Definition.ValidationRules[0].Expression.Conditions[0].Field);
    }

    [Fact]
    public async Task SwitchingTheJoinToAnyCommitsImmediately()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<ValidationRuleEditor>(p => p.Add(d => d.EditContext, context));
        await cut.Find("button.bf-validation-rules__add-button").ClickAsync(new MouseEventArgs());

        var anyRadio = cut.FindAll("input[type='radio']")[1];
        await anyRadio.ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.Equal(ConditionJoin.Any, context.Draft.Definition.ValidationRules[0].Expression.Join);
    }

    [Fact]
    public async Task RemovingAConditionCommitsTheShorterExpression()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<ValidationRuleEditor>(p => p.Add(d => d.EditContext, context));
        await cut.Find("button.bf-validation-rules__add-button").ClickAsync(new MouseEventArgs());
        await cut.Find("button.bf-validation-rules__button:not(.bf-validation-rules__remove-button)").ClickAsync(new MouseEventArgs());
        Assert.Single(context.Draft.Definition.ValidationRules[0].Expression.Conditions);

        await cut.Find("button.bf-condition-row__remove-button").ClickAsync(new MouseEventArgs());

        Assert.Empty(context.Draft.Definition.ValidationRules[0].Expression.Conditions);
    }

    [Fact]
    public async Task RemovingARuleCommitsTheShorterList()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<ValidationRuleEditor>(p => p.Add(d => d.EditContext, context));
        await cut.Find("button.bf-validation-rules__add-button").ClickAsync(new MouseEventArgs());
        Assert.Single(context.Draft.Definition.ValidationRules);

        await cut.Find("button.bf-validation-rules__remove-button").ClickAsync(new MouseEventArgs());

        Assert.Empty(context.Draft.Definition.ValidationRules);
    }

    // --- Focus management on add/remove (PRD §11, WCAG 2.4.3) --------------------------------

    private static ValidationRule RuleAgainst(string target, params ConditionOperator[] operators) => new()
    {
        Target = target,
        Message = string.Empty,
        Expression = new ConditionGroup { Conditions = [.. operators.Select(op => new Condition { Field = target, Operator = op })] },
    };

    [Fact]
    public async Task AddingARuleFocusesItsOwnTargetSelect()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<ValidationRuleEditor>(p => p.Add(d => d.EditContext, context));

        await cut.Find("button.bf-validation-rules__add-button").ClickAsync(new MouseEventArgs());

        Assert.Single(cut.FindAll("div.bf-validation-rules__rule"));
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 1);
    }

    [Fact]
    public async Task RemovingAMiddleRuleFocusesThePreviousRulesTargetSelect()
    {
        var definition = DesignerTestFixtures.TwoSectionDefinition("form-1") with
        {
            ValidationRules = [RuleAgainst("node-a", ConditionOperator.IsBlank), RuleAgainst("node-b", ConditionOperator.IsBlank), RuleAgainst("node-c", ConditionOperator.IsBlank)],
        };
        await using var context = CreateContext(definition);
        var cut = Render<ValidationRuleEditor>(p => p.Add(d => d.EditContext, context));

        var removeButtons = cut.FindAll("button.bf-validation-rules__remove-button");
        Assert.Equal(3, removeButtons.Count);
        await removeButtons[1].ClickAsync(new MouseEventArgs());

        Assert.Equal(2, context.Draft.Definition.ValidationRules.Count);
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 1);
    }

    [Fact]
    public async Task RemovingTheFirstRuleFocusesTheNewFirstRulesTargetSelect()
    {
        var definition = DesignerTestFixtures.TwoSectionDefinition("form-1") with
        {
            ValidationRules = [RuleAgainst("node-a", ConditionOperator.IsBlank), RuleAgainst("node-b", ConditionOperator.IsBlank)],
        };
        await using var context = CreateContext(definition);
        var cut = Render<ValidationRuleEditor>(p => p.Add(d => d.EditContext, context));

        await cut.FindAll("button.bf-validation-rules__remove-button")[0].ClickAsync(new MouseEventArgs());

        Assert.Equal("node-b", Assert.Single(context.Draft.Definition.ValidationRules).Target);
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 1);
    }

    [Fact]
    public async Task RemovingTheOnlyRemainingRuleFocusesTheAddRuleButton()
    {
        var definition = DesignerTestFixtures.TwoSectionDefinition("form-1") with
        {
            ValidationRules = [RuleAgainst("node-a", ConditionOperator.IsBlank)],
        };
        await using var context = CreateContext(definition);
        var cut = Render<ValidationRuleEditor>(p => p.Add(d => d.EditContext, context));

        await cut.Find("button.bf-validation-rules__remove-button").ClickAsync(new MouseEventArgs());

        Assert.Empty(context.Draft.Definition.ValidationRules);
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 1);
    }

    [Fact]
    public async Task AddingAConditionFocusesTheNewRowsFieldSelect()
    {
        var definition = DesignerTestFixtures.TwoSectionDefinition("form-1") with
        {
            ValidationRules = [new ValidationRule { Target = "node-a", Message = string.Empty, Expression = new ConditionGroup() }],
        };
        await using var context = CreateContext(definition);
        var cut = Render<ValidationRuleEditor>(p => p.Add(d => d.EditContext, context));

        await cut.Find("button.bf-validation-rules__button:not(.bf-validation-rules__remove-button)").ClickAsync(new MouseEventArgs());

        Assert.Single(cut.FindAll("div.bf-condition-row"));
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 1);
    }

    [Fact]
    public async Task RemovingAConditionWithSurvivorsFocusesThePreviousRowsFieldSelect()
    {
        var definition = DesignerTestFixtures.TwoSectionDefinition("form-1") with
        {
            ValidationRules = [RuleAgainst("node-a", ConditionOperator.IsBlank, ConditionOperator.IsNotBlank)],
        };
        await using var context = CreateContext(definition);
        var cut = Render<ValidationRuleEditor>(p => p.Add(d => d.EditContext, context));

        Assert.Equal(2, cut.FindAll("div.bf-condition-row").Count);
        await cut.FindAll("button.bf-condition-row__remove-button")[0].ClickAsync(new MouseEventArgs());

        Assert.Single(context.Draft.Definition.ValidationRules[0].Expression.Conditions);
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 1);
    }

    [Fact]
    public async Task RemovingTheLastConditionInARuleFocusesThatRulesAddConditionButton()
    {
        var definition = DesignerTestFixtures.TwoSectionDefinition("form-1") with
        {
            ValidationRules = [RuleAgainst("node-a", ConditionOperator.IsBlank)],
        };
        await using var context = CreateContext(definition);
        var cut = Render<ValidationRuleEditor>(p => p.Add(d => d.EditContext, context));

        await cut.Find("button.bf-condition-row__remove-button").ClickAsync(new MouseEventArgs());

        Assert.Empty(context.Draft.Definition.ValidationRules[0].Expression.Conditions);
        JSInterop.VerifyFocusAsyncInvoke(calledTimes: 1);
    }

    [Fact]
    public async Task RendersAnExistingRuleFromTheDefinition()
    {
        await using var context = CreateContext(DesignerTestFixtures.OptionNodeDefinition("form-1") with
        {
            ValidationRules =
            [
                new ValidationRule
                {
                    Target = "node-choice",
                    Message = "Pick an option.",
                    Expression = new ConditionGroup
                    {
                        Conditions = [new Condition { Field = "node-choice", Operator = ConditionOperator.IsBlank }],
                    },
                },
            ],
        });
        var cut = Render<ValidationRuleEditor>(p => p.Add(d => d.EditContext, context));

        Assert.Single(cut.FindAll("div.bf-validation-rules__rule"));
        Assert.Equal("Pick an option.", cut.Find("textarea").GetAttribute("value"));
        Assert.Single(cut.FindAll("div.bf-condition-row"));
    }
}
