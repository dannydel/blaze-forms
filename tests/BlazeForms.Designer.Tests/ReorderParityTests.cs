using BlazeForms.Canvas;
using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Serialization;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// The parity guarantee Phase 5 exists to prove (PRD §4.1): drag-and-drop, the
/// <c>Alt+↑/↓/←/→</c> keyboard paths, and the <c>Ctrl+M</c> dialog all funnel into the exact same
/// <see cref="DesignerEditContext.MoveNodeWithinSection"/>/<see cref="DesignerEditContext.MoveNodeAcrossSections"/>
/// calls, so the same logical move produces an identical resulting <see cref="FormDefinition"/>
/// regardless of which path an author used. Each test here starts from the same fixture, drives
/// exactly one path, and compares the resulting definition's JSON (<see cref="FormJson.SerializeDefinition"/>
/// gives structural equality independent of which list instances happen to survive the move --
/// the same idiom this repo's own golden-file tests use) against the other two paths' own results,
/// computed the same way in a sibling test.
/// </summary>
public sealed class ReorderParityTests : DesignerTestContext
{
    private static DesignerEditContext CreateContext(FormDefinition definition, IFormDefinitionStore? store = null) =>
        new(FormLifecycle.CreateDraft(definition), store ?? new InMemoryFormDefinitionStore());

    /// <summary>
    /// The one logical move every path below reaches by its own means: node-a, the sole
    /// occupant of section-1, ends up as the sole occupant of section-2 (previously empty).
    /// Appending to an empty section is simultaneously <c>Alt+→</c>'s own "end of the adjacent
    /// section" choice, the dialog's "position 1", and a drop onto the empty section's own
    /// container -- the one shape all three reorder paths can express identically without any
    /// path-specific move logic of its own.
    /// </summary>
    private static async Task<string> ExpectedResultJsonAsync()
    {
        var context = CreateContext(DesignerTestFixtures.TwoSectionSecondEmptyDefinition("form-1"));
        context.MoveNodeAcrossSections("node-a", "section-2", 0);
        var json = FormJson.SerializeDefinition(context.Draft.Definition);
        await context.DisposeAsync().ConfigureAwait(true);
        return json;
    }

    [Fact]
    public async Task DragAndDropAltArrowAndTheDialogAllProduceTheIdenticalResultingDefinition()
    {
        var expected = await ExpectedResultJsonAsync();

        // Path 1: drag-and-drop -- drop the sole row onto the empty section's own rows wrapper.
        await using var dragDropContext = CreateContext(DesignerTestFixtures.TwoSectionSecondEmptyDefinition("form-1"));
        var dragDropCut = Render<DesignerCanvas>(p => p.Add(c => c.EditContext, dragDropContext).Add(c => c.ActivePageId, "page-1"));
        var draggedRow = dragDropCut.Find("div.bf-canvas-row");
        var emptySectionRows = dragDropCut.FindAll("div.bf-canvas-section__rows")[1];
        await draggedRow.DragStartAsync(new DragEventArgs());
        await emptySectionRows.DropAsync(new DragEventArgs());
        Assert.Equal(expected, FormJson.SerializeDefinition(dragDropContext.Draft.Definition));

        // Path 2: Alt+→ -- the roving cursor defaults to the page's only row.
        await using var altArrowContext = CreateContext(DesignerTestFixtures.TwoSectionSecondEmptyDefinition("form-1"));
        var altArrowCut = Render<DesignerCanvas>(p => p.Add(c => c.EditContext, altArrowContext).Add(c => c.ActivePageId, "page-1"));
        await altArrowCut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight", AltKey = true });
        Assert.Equal(expected, FormJson.SerializeDefinition(altArrowContext.Draft.Definition));

        // Path 3: the Ctrl+M dialog -- section-2, position 1.
        await using var dialogContext = CreateContext(DesignerTestFixtures.TwoSectionSecondEmptyDefinition("form-1"));
        var dialogCut = Render<MoveToPositionDialog>(p => p.Add(d => d.EditContext, dialogContext).Add(d => d.NodeId, "node-a"));
        await dialogCut.Find("select#" + dialogCut.FindAll("select")[0].Id).ChangeAsync("section-2");
        await dialogCut.Find("form").SubmitAsync();
        Assert.Equal(expected, FormJson.SerializeDefinition(dialogContext.Draft.Definition));
    }

    /// <summary>
    /// The reference result for <see cref="SameSectionReorderProducesTheIdenticalResultingDefinitionAcrossAllThreePaths"/>:
    /// in <see cref="DesignerTestFixtures.TwoSectionDefinition"/>'s section-1
    /// (<c>[node-a, node-b, node-c]</c>), node-a ends up sitting immediately before node-c, i.e.
    /// post-removal index 1 -- the same-section case none of the lone-node/empty-section fixture
    /// above ever exercises: <see cref="DesignerCanvas"/>'s own-section <c>targetIndex - 1</c>
    /// drag-drop adjustment, the dialog's 1-based "position 2", and Alt+↓'s pre-removal-index
    /// arithmetic all have to agree on this exact slot.
    /// </summary>
    private static async Task<string> ExpectedSameSectionReorderResultJsonAsync()
    {
        var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        context.MoveNodeAcrossSections("node-a", "section-1", 1);
        var json = FormJson.SerializeDefinition(context.Draft.Definition);
        await context.DisposeAsync().ConfigureAwait(true);
        return json;
    }

    [Fact]
    public async Task SameSectionReorderProducesTheIdenticalResultingDefinitionAcrossAllThreePaths()
    {
        var expected = await ExpectedSameSectionReorderResultJsonAsync();

        // Path 1: drag-and-drop -- drag node-a (index 0) and drop it onto node-c (index 2).
        // DropOnRow's same-section adjustment shifts the target index down by one, since node-a
        // leaves section-1 before landing, so it comes to rest immediately before node-c.
        await using var dragDropContext = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var dragDropCut = Render<DesignerCanvas>(p => p.Add(c => c.EditContext, dragDropContext).Add(c => c.ActivePageId, "page-1"));
        var dragDropRows = dragDropCut.FindAll("div.bf-canvas-row");
        await dragDropRows[0].DragStartAsync(new DragEventArgs());
        await dragDropRows[2].DropAsync(new DragEventArgs());
        Assert.Equal(expected, FormJson.SerializeDefinition(dragDropContext.Draft.Definition));

        // Path 2: Alt+↓ -- the roving cursor defaults to node-a, section-1's first row; its
        // pre-removal-index arithmetic (RemoveAt(0), Insert(1)) reaches the same slot in a single
        // step.
        await using var altArrowContext = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var altArrowCut = Render<DesignerCanvas>(p => p.Add(c => c.EditContext, altArrowContext).Add(c => c.ActivePageId, "page-1"));
        await altArrowCut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });
        Assert.Equal(expected, FormJson.SerializeDefinition(altArrowContext.Draft.Definition));

        // Path 3: the Ctrl+M dialog -- section-1 unchanged (node-a's own section already), moved
        // to position 2, the dialog's 1-based name for the same post-removal index 1.
        await using var dialogContext = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var dialogCut = Render<MoveToPositionDialog>(p => p.Add(d => d.EditContext, dialogContext).Add(d => d.NodeId, "node-a"));
        await dialogCut.FindAll("select")[1].ChangeAsync("2");
        await dialogCut.Find("form").SubmitAsync();
        Assert.Equal(expected, FormJson.SerializeDefinition(dialogContext.Draft.Definition));
    }

    /// <summary>
    /// The reference result for
    /// <see cref="AppendingToANonEmptySectionProducesTheIdenticalResultingDefinitionAcrossAllThreePaths"/>:
    /// node-a moves from section-1 to the end of section-2, which already holds node-d -- the
    /// non-empty-target case none of the lone-node/empty-section fixture above ever exercises:
    /// <see cref="DesignerCanvas"/>'s own drag-drop "append at the section's current node count"
    /// fallback and the dialog's "count + 1" position have to agree that the node lands after
    /// node-d, not merely into an empty list.
    /// </summary>
    private static async Task<string> ExpectedAppendToNonEmptySectionResultJsonAsync()
    {
        var context = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        context.MoveNodeAcrossSections("node-a", "section-2", 1);
        var json = FormJson.SerializeDefinition(context.Draft.Definition);
        await context.DisposeAsync().ConfigureAwait(true);
        return json;
    }

    [Fact]
    public async Task AppendingToANonEmptySectionProducesTheIdenticalResultingDefinitionAcrossAllThreePaths()
    {
        var expected = await ExpectedAppendToNonEmptySectionResultJsonAsync();

        // Path 1: drag-and-drop -- drop onto section-2's own rows wrapper (below its one existing
        // row, node-d) rather than onto node-d's own row, so DropOnSection's append-to-the-end
        // fallback runs instead of DropOnRow's insert-before-target path.
        await using var dragDropContext = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var dragDropCut = Render<DesignerCanvas>(p => p.Add(c => c.EditContext, dragDropContext).Add(c => c.ActivePageId, "page-1"));
        var draggedRow = dragDropCut.FindAll("div.bf-canvas-row")[0]; // node-a, in section-1
        var section2Rows = dragDropCut.FindAll("div.bf-canvas-section__rows")[1]; // section-2's, holding node-d
        await draggedRow.DragStartAsync(new DragEventArgs());
        await section2Rows.DropAsync(new DragEventArgs());
        Assert.Equal(expected, FormJson.SerializeDefinition(dragDropContext.Draft.Definition));

        // Path 2: Alt+→ -- the roving cursor defaults to node-a; its own "always append to the
        // adjacent section's end" choice reaches the same result in a single step.
        await using var altArrowContext = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var altArrowCut = Render<DesignerCanvas>(p => p.Add(c => c.EditContext, altArrowContext).Add(c => c.ActivePageId, "page-1"));
        await altArrowCut.Find("div.bf-canvas").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight", AltKey = true });
        Assert.Equal(expected, FormJson.SerializeDefinition(altArrowContext.Draft.Definition));

        // Path 3: the Ctrl+M dialog -- section-2, defaulting its position select to "count + 1"
        // (2, one past node-d) the moment the section select changes, with no separate position
        // edit needed.
        await using var dialogContext = CreateContext(DesignerTestFixtures.TwoSectionDefinition("form-1"));
        var dialogCut = Render<MoveToPositionDialog>(p => p.Add(d => d.EditContext, dialogContext).Add(d => d.NodeId, "node-a"));
        await dialogCut.Find("select#" + dialogCut.FindAll("select")[0].Id).ChangeAsync("section-2");
        await dialogCut.Find("form").SubmitAsync();
        Assert.Equal(expected, FormJson.SerializeDefinition(dialogContext.Draft.Definition));
    }
}
