namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="DesignerSelection"/>'s factories and default: each anchors at exactly the
/// level it names and carries the intent it was given, and <see cref="DesignerSelection.None"/>
/// starts with every identifier unset.
/// </summary>
public sealed class DesignerSelectionTests
{
    [Fact]
    public void NoneHasNoIdentifiersAndNoIntent()
    {
        var selection = DesignerSelection.None;

        Assert.Null(selection.PageId);
        Assert.Null(selection.SectionId);
        Assert.Null(selection.NodeId);
        Assert.Equal(DesignerFocusIntent.None, selection.Intent);
    }

    [Fact]
    public void ForNodeCarriesItsPageAndSection()
    {
        var selection = DesignerSelection.ForNode("node-1", "page-1", "section-1", DesignerFocusIntent.NewNode);

        Assert.Equal("node-1", selection.NodeId);
        Assert.Equal("section-1", selection.SectionId);
        Assert.Equal("page-1", selection.PageId);
        Assert.Equal(DesignerFocusIntent.NewNode, selection.Intent);
    }

    [Fact]
    public void ForSectionLeavesNoNodeSelected()
    {
        var selection = DesignerSelection.ForSection("page-1", "section-1", DesignerFocusIntent.Neighbour);

        Assert.Null(selection.NodeId);
        Assert.Equal("section-1", selection.SectionId);
        Assert.Equal("page-1", selection.PageId);
        Assert.Equal(DesignerFocusIntent.Neighbour, selection.Intent);
    }

    [Fact]
    public void ForPageLeavesNoSectionOrNodeSelected()
    {
        var selection = DesignerSelection.ForPage("page-1", DesignerFocusIntent.NewNode);

        Assert.Null(selection.NodeId);
        Assert.Null(selection.SectionId);
        Assert.Equal("page-1", selection.PageId);
        Assert.Equal(DesignerFocusIntent.NewNode, selection.Intent);
    }
}
