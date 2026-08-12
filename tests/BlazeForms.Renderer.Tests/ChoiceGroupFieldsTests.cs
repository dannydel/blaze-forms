using BlazeForms.Definitions;
using BlazeForms.Fields;
using Bunit;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers the grouped choice components — <see cref="RadioGroupField"/>,
/// <see cref="YesNoField"/>, <see cref="CheckboxGroupField"/> — and the single-checkbox
/// <see cref="BooleanField"/>.
/// </summary>
public sealed class ChoiceGroupFieldsTests : BunitContext
{
    private static readonly FormOption[] Options =
    [
        new FormOption { Value = "opt-a", Label = "Option A, displayed" },
        new FormOption { Value = "opt-b", Label = "Option B, displayed" },
    ];

    [Fact]
    public void RadioGroupFieldRendersAFieldsetLegendAndOneLabelledRadioPerOption()
    {
        var node = TestNodes.Create(NodeType.Radio, label: "Pick one", required: true, options: Options);
        var cut = Render<RadioGroupField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        Assert.Equal("Pick one", cut.Find("legend").TextContent);

        // aria-required is invalid on a fieldset's implicit group role; it rides a
        // role="radiogroup" container that is named from the legend via aria-labelledby.
        var group = cut.Find("[role='radiogroup']");
        Assert.Equal("true", group.GetAttribute("aria-required"));
        Assert.Equal(cut.Find("legend").GetAttribute("id"), group.GetAttribute("aria-labelledby"));
        Assert.False(cut.Find("fieldset").HasAttribute("aria-required"));

        var radios = cut.FindAll("input[type='radio']");
        Assert.Equal(2, radios.Count);

        var labels = cut.FindAll("label");
        Assert.Equal("Option A, displayed", labels[0].TextContent);
        Assert.Equal(radios[0].GetAttribute("id"), labels[0].GetAttribute("for"));
    }

    [Fact]
    public void RadioGroupFieldGivesEachOptionAUniqueIdEvenWhenValuesSanitizeAlike()
    {
        // "a b" and "a/b" both collapse to "a-b" under a char-level sanitizer; index-keyed ids
        // must stay distinct so no <label for> binds to the wrong (or an ambiguous) input.
        FormOption[] colliding =
        [
            new FormOption { Value = "a b", Label = "Spaced" },
            new FormOption { Value = "a/b", Label = "Slashed" },
        ];
        var node = TestNodes.Create(NodeType.Radio, options: colliding);
        var cut = Render<RadioGroupField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        var ids = cut.FindAll("input[type='radio']").Select(r => r.GetAttribute("id")).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        var fors = cut.FindAll("label").Select(l => l.GetAttribute("for")).ToArray();
        Assert.Equal(ids, fors);
    }

    [Fact]
    public void RadioGroupFieldCheckingAnOptionRaisesValueChangedWithItsStoredValue()
    {
        object? captured = null;
        var node = TestNodes.Create(NodeType.Radio, options: Options);
        var cut = Render<RadioGroupField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.ValueChanged, v => captured = v));

        cut.FindAll("input[type='radio']")[1].Change(true);

        Assert.Equal("opt-b", captured);
    }

    [Fact]
    public void RadioGroupFieldErrorMarksEveryRadioInvalidAndDescribesTheFieldset()
    {
        var node = TestNodes.Create(NodeType.Radio, options: Options);
        var cut = Render<RadioGroupField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.Error, "Pick one."));

        Assert.All(cut.FindAll("input[type='radio']"), radio => Assert.Equal("true", radio.GetAttribute("aria-invalid")));
        Assert.Equal("f1-error", cut.Find("[role='radiogroup']").GetAttribute("aria-describedby"));
    }

    [Fact]
    public void YesNoFieldRendersItsOwnComponentTypeWithTheSameGroupSemantics()
    {
        var node = TestNodes.Create(NodeType.YesNo, label: "Do you consent?", options: Options);
        var cut = Render<YesNoField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        Assert.Equal("Do you consent?", cut.Find("legend").TextContent);
        Assert.Equal(2, cut.FindAll("input[type='radio']").Count);
        var group = cut.Find("[role='radiogroup']");
        Assert.Equal(cut.Find("legend").GetAttribute("id"), group.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void YesNoFieldCheckingAnOptionRaisesValueChangedWithItsStoredValue()
    {
        object? captured = null;
        var node = TestNodes.Create(NodeType.YesNo, options: Options);
        var cut = Render<YesNoField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.ValueChanged, v => captured = v));

        cut.FindAll("input[type='radio']")[0].Change(true);

        Assert.Equal("opt-a", captured);
    }

    [Fact]
    public void CheckboxGroupFieldRendersAFieldsetLegendAndOneLabelledCheckboxPerOption()
    {
        var node = TestNodes.Create(NodeType.CheckboxGroup, label: "Pick any", options: Options);
        var cut = Render<CheckboxGroupField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        Assert.Equal("Pick any", cut.Find("legend").TextContent);
        Assert.Equal(2, cut.FindAll("input[type='checkbox']").Count);

        // A checkbox set has no group-level required semantic AT would honor, so no
        // aria-required is emitted on the fieldset (it would be dropped as invalid on "group").
        Assert.False(cut.Find("fieldset").HasAttribute("aria-required"));
    }

    [Fact]
    public void CheckboxGroupFieldCheckingAddsTheStoredValueAndUncheckingRemovesIt()
    {
        object? captured = null;
        var node = TestNodes.Create(NodeType.CheckboxGroup, options: Options);
        var cut = Render<CheckboxGroupField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.Value, new List<string> { "opt-a" })
            .Add(f => f.ValueChanged, v => captured = v));

        cut.FindAll("input[type='checkbox']")[1].Change(true);
        var afterCheck = Assert.IsAssignableFrom<IReadOnlyList<string>>(captured);
        Assert.Equal(["opt-a", "opt-b"], afterCheck);

        cut.Render(p => p.Add(f => f.Value, afterCheck));
        cut.FindAll("input[type='checkbox']")[0].Change(false);
        var afterUncheck = Assert.IsAssignableFrom<IReadOnlyList<string>>(captured);
        Assert.Equal(["opt-b"], afterUncheck);
    }

    [Fact]
    public void BooleanFieldRendersASingleCheckboxLabelledByFieldId()
    {
        var node = TestNodes.Create(NodeType.Boolean, label: "I agree", required: true);
        var cut = Render<BooleanField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1"));

        var checkbox = cut.Find("input[type='checkbox']");
        Assert.Equal("f1", checkbox.GetAttribute("id"));
        Assert.Equal("true", checkbox.GetAttribute("aria-required"));
        Assert.Equal("f1", cut.Find("label").GetAttribute("for"));
    }

    [Fact]
    public void BooleanFieldTogglingRaisesValueChangedAsBool()
    {
        object? captured = null;
        var node = TestNodes.Create(NodeType.Boolean);
        var cut = Render<BooleanField>(p => p
            .Add(f => f.Node, node)
            .Add(f => f.FieldId, "f1")
            .Add(f => f.ValueChanged, v => captured = v));

        cut.Find("input[type='checkbox']").Change(true);

        Assert.Equal(true, captured);
    }
}
