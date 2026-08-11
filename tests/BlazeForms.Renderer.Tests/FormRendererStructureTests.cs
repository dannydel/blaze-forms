using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Fields;
using BlazeForms.Hosting;
using Bunit;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="FormRenderer"/>'s structure: pages as steps behind a progress header,
/// sections as <c>fieldset</c>/<c>legend</c>, step navigation, the registry-first component
/// resolver, and the P1 rule that a <see cref="NodeType.Calc"/> node renders but never carries a
/// value (PRD §4.2, §5, §10).
/// </summary>
public sealed class FormRendererStructureTests : BunitContext
{
    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by the renderer's DynamicComponent through Type identity, not directly by this test.")]
    private sealed class MarkerField : FormFieldBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "data-marker-field", "true");
            builder.AddAttribute(2, "id", FieldId);
            builder.CloseElement();
        }
    }

    private sealed class StubFieldComponentRegistry : IFieldComponentRegistry
    {
        private readonly Dictionary<NodeType, Type> _registrations = [];

        public void Register(NodeType nodeType, Type componentType) => _registrations[nodeType] = componentType;

        public bool TryGetComponentType(NodeType nodeType, out Type? componentType) =>
            _registrations.TryGetValue(nodeType, out componentType);
    }

    [Fact]
    public void OnlyTheActiveStepsFieldsAreRendered()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoStepDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        Assert.Equal(2, cut.FindAll("input[type='text']").Count);
        Assert.Empty(cut.FindAll("textarea"));
        Assert.Equal("About you", cut.Find("h2").TextContent.Trim());
    }

    [Fact]
    public void ProgressHeaderListsEveryPageAndMarksTheActiveOneWithAriaCurrentStep()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoStepDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        var steps = cut.FindAll("li.bf-progress__step");
        Assert.Equal(2, steps.Count);
        Assert.Equal("step", steps[0].GetAttribute("aria-current"));
        Assert.Null(steps[1].GetAttribute("aria-current"));
        Assert.Contains("About you", steps[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Preferences", steps[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void StepChangeIsAnnouncedInAPoliteLiveRegion()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoStepDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        var liveRegion = cut.Find("[aria-live='polite']");
        Assert.Equal("Step 1 of 2: About you", liveRegion.TextContent);

        cut.FindAll("button")[1].Click();

        liveRegion = cut.Find("[aria-live='polite']");
        Assert.Equal("Step 2 of 2: Preferences", liveRegion.TextContent);
    }

    [Fact]
    public void NextButtonAdvancesAStepUnguardedAndPreviousReturns()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoStepDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.FindAll("button")[1].Click(); // Next

        Assert.Equal("Preferences", cut.Find("h2").TextContent.Trim());
        Assert.NotEmpty(cut.FindAll("textarea"));

        cut.FindAll("button")[0].Click(); // Previous

        Assert.Equal("About you", cut.Find("h2").TextContent.Trim());
    }

    [Fact]
    public void PreviousIsDisabledOnTheFirstStepAndNextIsDisabledOnTheLastStep()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoStepDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        var buttons = cut.FindAll("button");
        Assert.True(buttons[0].HasAttribute("disabled"));
        Assert.False(buttons[1].HasAttribute("disabled"));

        buttons[1].Click();

        buttons = cut.FindAll("button");
        Assert.False(buttons[0].HasAttribute("disabled"));
        Assert.True(buttons[1].HasAttribute("disabled"));
    }

    [Fact]
    public void SectionsRenderAsAFieldsetWithALegendAndDescription()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoStepDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        var fieldset = cut.Find("fieldset.bf-section");
        Assert.Equal("Your details", fieldset.QuerySelector("legend")!.TextContent);
        Assert.Equal("We use this to reach you.", fieldset.QuerySelector("p.bf-section__description")!.TextContent);
    }

    [Fact]
    public void AHostSuppliedFieldComponentRegistryOverrideIsHonored()
    {
        var registry = new StubFieldComponentRegistry();
        registry.Register(NodeType.Text, typeof(MarkerField));
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoFieldDefinition);

        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.FieldComponents, registry));

        Assert.Equal(2, cut.FindAll("[data-marker-field]").Count);
        Assert.Empty(cut.FindAll("input[type='text']"));
    }

    [Fact]
    public void CalcNodeRendersButIsNeverWiredToAValueOrValueChanged()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.CalcDefinition);

        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        Assert.NotNull(cut.Find("#estimate"));

        // The renderer never seeds a payload key for a calc node (PRD §5): the direct proof, at
        // the component-wiring level, is that CalcField never receives a Value parameter in the
        // first place, so there is nothing for it to have echoed back through ValueChanged.
        var calcField = cut.FindComponent<CalcField>();
        Assert.Null(calcField.Instance.Value);
    }
}
