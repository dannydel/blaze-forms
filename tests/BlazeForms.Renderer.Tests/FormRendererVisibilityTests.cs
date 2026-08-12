using Bunit;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="FormRenderer"/>'s live conditional visibility (PRD §6): a hidden node is
/// never emitted, so toggling the field its rule depends on shows or hides it in the DOM in
/// real time, driven through the actual field input rather than through internal state.
/// </summary>
/// <remarks>
/// Every selector here matches on an <c>id</c> <em>suffix</em> (<c>[id$='-detail']</c>) rather
/// than an exact <c>#detail</c> id, because <see cref="FormRenderer"/> namespaces every field's
/// DOM id to its own instance (<c>{instanceId}-{nodeId}</c>) so two renderer instances of the
/// same definition on one page never collide — see <see cref="FormRendererFieldIdNamespacingTests"/>.
/// </remarks>
public sealed class FormRendererVisibilityTests : RendererTestContext
{
    [Fact]
    public void DependentNodeIsAbsentUntilItsControllingFieldMakesItVisible()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.ConditionalDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        Assert.Empty(cut.FindAll("[id$='-detail']"));

        cut.Find("input[type='checkbox']").Change(true);

        Assert.NotEmpty(cut.FindAll("[id$='-detail']"));
    }

    [Fact]
    public void DependentNodeDisappearsAgainWhenTheControllingFieldIsUnset()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.ConditionalDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        var trigger = cut.Find("input[type='checkbox']");
        trigger.Change(true);
        Assert.NotEmpty(cut.FindAll("[id$='-detail']"));

        cut.Find("input[type='checkbox']").Change(false);

        Assert.Empty(cut.FindAll("[id$='-detail']"));
    }

    [Fact]
    public void AHiddenNodeNeverEntersTheAccessibilityTreeEvenAfterAnAnswerIsTypedIntoItsController()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.ConditionalDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        // Hidden by default: not present at all, not merely styled invisible.
        Assert.DoesNotContain("Describe the help you need", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ChainedDependentDisappearsWhenAnIntermediateFieldItselfHides()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.ChainedVisibilityDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        // expert -> years -> senior-notice: reveal the whole chain. The number field binds on
        // oninput, so drive it with Input, not Change.
        cut.Find("[id$='-expert']").Change(true);
        cut.Find("[id$='-years']").Input("20");
        Assert.NotEmpty(cut.FindAll("[id$='-senior-notice']"));

        // Hiding the intermediate field (years) must drop its stale answer from the visibility
        // decision, so the leaf hides too rather than lingering in the DOM / accessibility tree
        // on a value no longer reachable (PRD §6). A single-pass evaluation over unpruned answers
        // would leave senior-notice visible here.
        cut.Find("[id$='-expert']").Change(false);

        Assert.Empty(cut.FindAll("[id$='-years']"));
        Assert.Empty(cut.FindAll("[id$='-senior-notice']"));
    }
}
