using BlazeForms.Definitions;
using BlazeForms.Fields;
using Bunit;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers the four single-line/multi-line text inputs — <see cref="TextField"/>,
/// <see cref="TextAreaField"/>, <see cref="EmailField"/>, <see cref="PhoneField"/> — whose markup
/// and a11y wiring are otherwise identical apart from element/type/inputmode/autocomplete.
/// </summary>
public sealed class TextInputFieldsTests : BunitContext
{
    [Fact]
    public void TextFieldRendersATextInputLabelledByFieldId()
    {
        var node = TestNodes.Create(NodeType.Text, label: "Full name", required: true);
        var cut = Render<TextField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f-name"));

        var input = cut.Find("input");
        Assert.Equal("text", input.GetAttribute("type"));
        Assert.Equal("f-name", input.GetAttribute("id"));
        Assert.Equal("true", input.GetAttribute("aria-required"));

        var label = cut.Find("label");
        Assert.Equal("f-name", label.GetAttribute("for"));
        Assert.Equal("Full name", label.TextContent);
    }

    [Fact]
    public void TextFieldTypingRaisesValueChangedWithTheTypedString()
    {
        object? captured = "unset";
        var node = TestNodes.Create(NodeType.Text);
        var cut = Render<TextField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.ValueChanged, v => captured = v));

        cut.Find("input").Input("Jordan");

        Assert.Equal("Jordan", captured);
    }

    [Fact]
    public void TextFieldClearingTheInputRaisesValueChangedWithNull()
    {
        object? captured = "unset";
        var node = TestNodes.Create(NodeType.Text);
        var cut = Render<TextField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.Value, "was set")
            .Add(f => f.ValueChanged, v => captured = v));

        cut.Find("input").Input("");

        Assert.Null(captured);
    }

    [Fact]
    public void TextFieldErrorSetsAriaInvalidAndDescribedByTheErrorElement()
    {
        var node = TestNodes.Create(NodeType.Text);
        var cut = Render<TextField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.Error, "Enter a full name."));

        var input = cut.Find("input");
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Equal("f1-error", input.GetAttribute("aria-describedby"));
        Assert.Equal("Enter a full name.", cut.Find("#f1-error").TextContent);
    }

    [Fact]
    public void TextFieldNoErrorOmitsAriaInvalid()
    {
        var node = TestNodes.Create(NodeType.Text);
        var cut = Render<TextField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        Assert.Null(cut.Find("input").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void TextFieldHelpAndErrorBothPresentAreBothInAriaDescribedBy()
    {
        var node = TestNodes.Create(NodeType.Text, help: "We use this to contact you.");
        var cut = Render<TextField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.Error, "Required."));

        Assert.Equal("f1-help f1-error", cut.Find("input").GetAttribute("aria-describedby"));
    }

    [Fact]
    public void TextFieldHelpRendersSanitizedMarkdownAndStripsAnInjectedScript()
    {
        var node = TestNodes.Create(NodeType.Text, help: "Visit <script>alert(1)</script> our [site](javascript:alert(2)).");
        var cut = Render<TextField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        var help = cut.Find("#f1-help").InnerHtml;
        Assert.DoesNotContain("<script", help, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", help, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TextFieldDoesNotReRenderWhenParametersAreUnchanged()
    {
        var node = TestNodes.Create(NodeType.Text);
        var cut = Render<TextField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.Value, "same"));

        var rendersBefore = cut.RenderCount;

        cut.Render(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.Value, "same"));

        Assert.Equal(rendersBefore, cut.RenderCount);
    }

    [Fact]
    public void TextAreaFieldRendersATextareaElement()
    {
        var node = TestNodes.Create(NodeType.TextArea, label: "Notes");
        var cut = Render<TextAreaField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        Assert.NotEmpty(cut.FindAll("textarea"));
        Assert.Equal("f1", cut.Find("label").GetAttribute("for"));
    }

    [Fact]
    public void EmailFieldRendersEmailTypeInputmodeAndAutocomplete()
    {
        var node = TestNodes.Create(NodeType.Email, label: "Email");
        var cut = Render<EmailField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        var input = cut.Find("input");
        Assert.Equal("email", input.GetAttribute("type"));
        Assert.Equal("email", input.GetAttribute("inputmode"));
        Assert.Equal("email", input.GetAttribute("autocomplete"));
    }

    [Fact]
    public void PhoneFieldRendersTelTypeInputmodeAndAutocomplete()
    {
        var node = TestNodes.Create(NodeType.Phone, label: "Phone");
        var cut = Render<PhoneField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        var input = cut.Find("input");
        Assert.Equal("tel", input.GetAttribute("type"));
        Assert.Equal("tel", input.GetAttribute("inputmode"));
        Assert.Equal("tel", input.GetAttribute("autocomplete"));
    }
}
