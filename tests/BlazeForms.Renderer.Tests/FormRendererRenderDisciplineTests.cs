using BlazeForms.Fields;
using Bunit;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Proves <see cref="FormRenderer"/> preserves the field components' own render discipline
/// (AGENTS.md "Render discipline"): a keystroke in one field must not re-render a sibling field,
/// even though the renderer's own re-render regenerates the whole current step's render tree
/// every time an answer changes.
/// </summary>
public sealed class FormRendererRenderDisciplineTests : BunitContext
{
    [Fact]
    public void TypingInOneFieldDoesNotReRenderASiblingField()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoFieldDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        var fields = cut.FindComponents<TextField>();
        Assert.Equal(2, fields.Count);
        var fieldA = fields[0];
        var fieldB = fields[1];

        var fieldARendersBefore = fieldA.RenderCount;
        var fieldBRendersBefore = fieldB.RenderCount;

        cut.FindAll("input[type='text']")[0].Input("hello");

        Assert.True(fieldA.RenderCount > fieldARendersBefore);
        Assert.Equal(fieldBRendersBefore, fieldB.RenderCount);
    }

    [Fact]
    public void TypingInOneFieldTwiceDoesNotAccumulateExtraSiblingRenders()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.TwoFieldDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        var fieldB = cut.FindComponents<TextField>()[1];
        var fieldBRendersBefore = fieldB.RenderCount;

        // Re-find between calls: each render replaces the event handler bound to the input, so
        // reusing a stale IElement handle from before a render throws in bUnit.
        cut.FindAll("input[type='text']")[0].Input("h");
        cut.FindAll("input[type='text']")[0].Input("he");
        cut.FindAll("input[type='text']")[0].Input("hel");

        Assert.Equal(fieldBRendersBefore, fieldB.RenderCount);
    }
}
