using BlazeForms.Canvas;
using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="MoveToPositionDialog"/> in isolation: it seeds its section and position
/// selects from the node's current location, updates the position options when the section
/// select changes, confirms through <see cref="DesignerEditContext.MoveNodeToPosition"/> and
/// closes, cancels (<c>Esc</c> and the Cancel button) without touching the draft, and satisfies
/// its own <c>role="dialog" aria-modal="true"</c>/focus-trap contract (PRD §4.1, §11). Coverage
/// of the <c>Ctrl+M</c> trigger and the post-close focus destination lives in
/// <c>DesignerCanvasTests</c>, since both are <see cref="Canvas.DesignerCanvas"/>'s own
/// responsibility, not this dialog's.
/// </summary>
public sealed class MoveToPositionDialogTests : DesignerTestContext
{
    private static DesignerEditContext CreateContext(FormDefinition definition, IFormDefinitionStore? store = null) =>
        new(FormLifecycle.CreateDraft(definition), store ?? new InMemoryFormDefinitionStore());

    [Fact]
    public async Task RendersAsAFocusLabelledModalDialog()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<MoveToPositionDialog>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-a"));

        var dialog = cut.Find("div.bf-move-dialog");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        var labelledBy = dialog.GetAttribute("aria-labelledby");
        Assert.NotNull(labelledBy);
        Assert.Equal(cut.Find("h2").Id, labelledBy);
    }

    [Fact]
    public async Task SectionAndPositionSelectsPopulateFromTheNodesCurrentLocation()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        // node-c is the third (index 2) of three nodes in section-1.
        var cut = Render<MoveToPositionDialog>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-c"));

        var sectionOptions = cut.FindAll("select")[0].QuerySelectorAll("option");
        Assert.Equal(2, sectionOptions.Length);
        Assert.Equal(["section-1", "section-2"], sectionOptions.Select(o => o.GetAttribute("value")));

        var sectionSelect = (AngleSharp.Html.Dom.IHtmlSelectElement)cut.FindAll("select")[0];
        Assert.Equal("section-1", sectionSelect.Value);

        var positionOptions = cut.FindAll("select")[1].QuerySelectorAll("option");
        Assert.Equal(3, positionOptions.Length); // three nodes already in section-1
        var positionSelect = (AngleSharp.Html.Dom.IHtmlSelectElement)cut.FindAll("select")[1];
        Assert.Equal("3", positionSelect.Value);
    }

    [Fact]
    public async Task PositionOptionsUpdateWhenTheSectionSelectChanges()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<MoveToPositionDialog>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-a"));

        // section-2 has one node (node-d) that is not the one being moved -- two slots (before
        // and after it) once node-a is excluded from its own section's count.
        await cut.Find("select#" + cut.FindAll("select")[0].Id).ChangeAsync("section-2");

        var positionSelect = (AngleSharp.Html.Dom.IHtmlSelectElement)cut.FindAll("select")[1];
        Assert.Equal(2, positionSelect.QuerySelectorAll("option").Length);

        // Switching sections defaults the position to that section's own end -- the same
        // "append" choice DesignerCanvas's own Alt+←/→ path makes.
        Assert.Equal("2", positionSelect.Value);
    }

    [Fact]
    public async Task ConfirmMovesTheNodeAndRaisesOnClosed()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var closed = false;
        var cut = Render<MoveToPositionDialog>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.NodeId, "node-c")
            .Add(d => d.OnClosed, () => closed = true));

        await cut.Find("select#" + cut.FindAll("select")[0].Id).ChangeAsync("section-2");
        await cut.Find("select#" + cut.FindAll("select")[1].Id).ChangeAsync("1");
        await cut.Find("form").SubmitAsync();

        Assert.Equal(["node-a", "node-b"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id));
        Assert.Equal(["node-c", "node-d"], context.Draft.Definition.Pages[0].Sections[1].Nodes.Select(n => n.Id));
        Assert.True(closed);
        Assert.Equal(DesignerFocusIntent.Moved, context.Selection.Intent);
    }

    [Fact]
    public async Task EscCancelsWithoutTouchingTheDraftAndRaisesOnClosed()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var closed = false;
        var cut = Render<MoveToPositionDialog>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.NodeId, "node-a")
            .Add(d => d.OnClosed, () => closed = true));

        await cut.Find("div.bf-move-dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(["node-a", "node-b", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id));
        Assert.Equal(DesignerSelection.None, context.Selection);
        Assert.True(closed);
    }

    [Fact]
    public async Task CancelButtonCancelsWithoutTouchingTheDraft()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var closed = false;
        var cut = Render<MoveToPositionDialog>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.NodeId, "node-a")
            .Add(d => d.OnClosed, () => closed = true));

        await cut.Find("button[type='button']").ClickAsync(new MouseEventArgs());

        Assert.Equal(["node-a", "node-b", "node-c"], context.Draft.Definition.Pages[0].Sections[0].Nodes.Select(n => n.Id));
        Assert.True(closed);
    }

    [Fact]
    public async Task ConfirmingWithNoChangeIsAHarmlessNoOp()
    {
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        DesignerAnnouncement? announcement = null;
        context.Announced += a => announcement = a;
        var closed = false;
        var cut = Render<MoveToPositionDialog>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.NodeId, "node-a")
            .Add(d => d.OnClosed, () => closed = true));

        await cut.Find("form").SubmitAsync();

        Assert.Null(announcement);
        Assert.True(closed);
    }

    [Fact]
    public async Task TheFocusTrapModuleIsImportedAndDisposed()
    {
        var module = JSInterop.SetupModule(MoveToPositionDialog.ModulePath);
        await using var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var cut = Render<MoveToPositionDialog>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-a"));

        cut.WaitForAssertion(() => Assert.True(cut.Instance.HasImportedModule));
        module.VerifyInvoke("attachFocusTrap");
        JSInterop.VerifyFocusAsyncInvoke();

        await cut.Instance.DisposeAsync();

        Assert.False(cut.Instance.HasImportedModule);
    }
}
