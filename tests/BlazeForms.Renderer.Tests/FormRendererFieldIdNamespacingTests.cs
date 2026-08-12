using Bunit;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers §6 of the phase-4 task: two <see cref="FormRenderer"/> instances of the same
/// definition on one host page must never collide on a field's DOM <c>id</c>, and each
/// instance's error summary must anchor only to its own fields.
/// </summary>
public sealed class FormRendererFieldIdNamespacingTests : RendererTestContext
{
    [Fact]
    public void TwoInstancesOfTheSameDefinitionProduceDisjointFieldDomIds()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoRequiredFieldsDefinition);

        var cut = Render(builder =>
        {
            builder.OpenComponent<FormRenderer>(0);
            builder.AddAttribute(1, "Version", version);
            builder.CloseComponent();
            builder.OpenComponent<FormRenderer>(2);
            builder.AddAttribute(3, "Version", version);
            builder.CloseComponent();
        });

        var ids = cut.FindAll("input[type='text']").Select(input => input.GetAttribute("id")).ToList();

        Assert.Equal(4, ids.Count); // Two fields per instance, two instances.
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EachInstancesSummaryAnchorsResolveOnlyWithinItsOwnInstance()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoRequiredFieldsDefinition);

        var cut = Render(builder =>
        {
            builder.OpenComponent<FormRenderer>(0);
            builder.AddAttribute(1, "Version", version);
            builder.CloseComponent();
            builder.OpenComponent<FormRenderer>(2);
            builder.AddAttribute(3, "Version", version);
            builder.CloseComponent();
        });

        var renderers = cut.FindComponents<FormRenderer>();
        Assert.Equal(2, renderers.Count);

        foreach (var renderer in renderers)
        {
            renderer.FindAll("button")[1].Click(); // Next, blocked in every instance alike.

            var summaryLinks = renderer.Find("[role='alert']").QuerySelectorAll("a");
            var ownFieldIds = renderer.FindAll("input[type='text']").Select(input => input.GetAttribute("id")).ToHashSet(StringComparer.Ordinal);

            Assert.Equal(2, summaryLinks.Length);

            foreach (var link in summaryLinks)
            {
                var targetId = link.GetAttribute("href")!.TrimStart('#');
                Assert.Contains(targetId, ownFieldIds);
            }
        }
    }
}
