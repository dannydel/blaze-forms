using BlazeForms.Definitions;
using BlazeForms.Palette;
using Bunit;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="FieldPalette"/>'s grouping (PRD §5's node-type table), the disabled
/// affordance for the three P2-reserved types, search filtering, and the accessibility contract
/// its search box and disabled entries carry.
/// </summary>
public sealed class FieldPaletteTests : DesignerTestContext
{
    [Fact]
    public void RendersEveryNodeTypeExactlyOnceAcrossItsGroups()
    {
        var cut = Render<FieldPalette>();

        var labels = cut.FindAll("span.bf-palette__item-label");
        Assert.Equal(Enum.GetValues<NodeType>().Length, labels.Count);
    }

    [Fact]
    public void EighteenPhaseOneTypesRenderAddableAndNotDisabled()
    {
        var cut = Render<FieldPalette>();

        foreach (var p1 in FormSchema.PhaseOneNodeTypes)
        {
            var button = FindButton(cut, ExpectedLabel(p1));
            Assert.False(button.HasAttribute("disabled"));
            Assert.False(button.HasAttribute("aria-disabled"));
        }

        // The reverse direction: every addable (non-disabled) entry in the palette is one of
        // FormSchema.PhaseOneNodeTypes -- not just that every P1 type happens to be addable, but
        // that the addable set is exactly that set and nothing more. Without this, a future Core
        // reclassification that shrinks PhaseOneNodeTypes without a matching palette change would
        // pass the loop above yet silently leave a now-reserved type still addable here.
        var addableTypes = Enum.GetValues<NodeType>()
            .Where(nodeType => !FindButton(cut, ExpectedLabel(nodeType)).HasAttribute("disabled"))
            .ToArray();
        Assert.Equal(
            FormSchema.PhaseOneNodeTypes.OrderBy(t => t).ToArray(),
            addableTypes.OrderBy(t => t).ToArray());
    }

    [Fact]
    public void ThreeReservedTypesRenderDisabledWithAPhaseBadgeAndAreAriaDisabled()
    {
        var cut = Render<FieldPalette>();

        Assert.Equal(3, FormSchema.ReservedNodeTypes.Count);

        foreach (var reserved in FormSchema.ReservedNodeTypes)
        {
            var button = FindButton(cut, ExpectedLabel(reserved));

            Assert.True(button.HasAttribute("disabled"));
            Assert.Equal("true", button.GetAttribute("aria-disabled"));
            Assert.Contains(
                "Phase 2",
                button.QuerySelector("span.bf-palette__badge")!.TextContent,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReservedTypesLiveInTheAdvancedGroupAlongsideCalc()
    {
        var cut = Render<FieldPalette>();

        var advancedGroup = cut.FindAll("section.bf-palette__group")
            .Single(section => section.QuerySelector("h3")!.TextContent == "Advanced");
        var advancedLabels = advancedGroup.QuerySelectorAll("span.bf-palette__item-label")
            .Select(span => span.TextContent)
            .ToArray();

        Assert.Equal(1 + FormSchema.ReservedNodeTypes.Count, advancedLabels.Length);
        Assert.Contains(ExpectedLabel(NodeType.Calc), advancedLabels);

        foreach (var reserved in FormSchema.ReservedNodeTypes)
        {
            Assert.Contains(ExpectedLabel(reserved), advancedLabels);
        }
    }

    [Fact]
    public void SearchFiltersEntriesByDisplayNameAndHidesEmptyGroups()
    {
        var cut = Render<FieldPalette>();

        cut.Find("input[type='search']").Input("email");

        var labels = cut.FindAll("span.bf-palette__item-label");
        Assert.Single(labels);
        Assert.Equal("Email", labels[0].TextContent);

        // "Email" only ever appears in the Text group -- every other group has nothing left to
        // show once filtered, so it disappears from the DOM entirely rather than rendering empty.
        var groupTitles = cut.FindAll("h3.bf-palette__group-title").Select(h => h.TextContent);
        Assert.Equal(["Text"], groupTitles);
    }

    [Fact]
    public void ClearingTheSearchRestoresEveryEntry()
    {
        var cut = Render<FieldPalette>();

        cut.Find("input[type='search']").Input("email");
        cut.Find("input[type='search']").Input(string.Empty);

        Assert.Equal(Enum.GetValues<NodeType>().Length, cut.FindAll("span.bf-palette__item-label").Count);
    }

    [Fact]
    public void TheSearchInputHasAProgrammaticLabel()
    {
        var cut = Render<FieldPalette>();

        var input = cut.Find("input[type='search']");
        var label = cut.Find($"label[for='{input.Id}']");

        Assert.Equal("Search fields", label.TextContent);
    }

    [Fact]
    public void SelectingAnAddableEntryRaisesOnAddRequestedWithItsNodeType()
    {
        NodeType? requested = null;
        var cut = Render<FieldPalette>(p => p.Add(f => f.OnAddRequested, type => requested = type));

        FindButton(cut, ExpectedLabel(NodeType.Email)).Click();

        Assert.Equal(NodeType.Email, requested);
    }

    [Fact]
    public void SelectingAReservedEntryNeverRaisesOnAddRequested()
    {
        var raised = false;
        var cut = Render<FieldPalette>(p => p.Add(f => f.OnAddRequested, _ => raised = true));
        var button = FindButton(cut, ExpectedLabel(NodeType.Repeating));

        // A disabled native <button> never gets an @onclick handler wired to it in the first
        // place -- bUnit refuses to dispatch a click at all, which is stronger proof of
        // "not addable" than a click that would otherwise just silently no-op.
        Assert.Throws<MissingEventHandlerException>(() => button.Click());
        Assert.False(raised);
    }

    private static AngleSharp.Dom.IElement FindButton(IRenderedComponent<FieldPalette> cut, string label) =>
        cut.FindAll("span.bf-palette__item-label")
            .Single(span => span.TextContent == label)
            .ParentElement!;

    private static string ExpectedLabel(NodeType nodeType) => nodeType switch
    {
        NodeType.Text => "Text",
        NodeType.TextArea => "Text area",
        NodeType.Email => "Email",
        NodeType.Phone => "Phone",
        NodeType.Number => "Number",
        NodeType.Currency => "Currency",
        NodeType.Date => "Date",
        NodeType.DateRange => "Date range",
        NodeType.Select => "Dropdown",
        NodeType.Radio => "Radio buttons",
        NodeType.CheckboxGroup => "Checkbox group",
        NodeType.YesNo => "Yes/No",
        NodeType.Boolean => "Checkbox",
        NodeType.Heading => "Heading",
        NodeType.Paragraph => "Paragraph",
        NodeType.Callout => "Callout",
        NodeType.Divider => "Divider",
        NodeType.Calc => "Calculated value",
        NodeType.Repeating => "Repeating group",
        NodeType.File => "File upload",
        NodeType.Lookup => "Lookup",
        _ => throw new NotSupportedException($"No expected label mapped for {nodeType}."),
    };
}
