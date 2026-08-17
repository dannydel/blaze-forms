using BlazeForms.Definitions;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Designer.Internal;

/// <summary>
/// Drills <see cref="DesignerEditContext.Selection"/> into, or back out of, a repeating group's
/// own drill-in scope (repeating-groups-plan.md, Increment C, PRD §4.1, §11). Shared by
/// <see cref="Canvas.DesignerCanvas"/>'s own <c>→</c>/<c>Esc</c> canvas commands and
/// <see cref="Properties.PropertiesPanel"/>'s "Edit group fields" button -- the two entry points
/// D-4 asks for -- so both land on the exact same selection shape and announcement rather than
/// each growing its own copy.
/// </summary>
/// <remarks>
/// Neither <see cref="Enter"/> nor <see cref="Exit"/> touches <see cref="DesignerEditContext.Draft"/>:
/// scoping the canvas is a view change, not a definition mutation, so it goes through
/// <see cref="DesignerEditContext.Select"/> (no undo entry, no autosave) and
/// <see cref="DesignerEditContext.Announce"/> directly, mirroring the split
/// <see cref="DesignerEditContext.Select"/>'s own remarks already draw between "what is selected"
/// and "what changed".
/// </remarks>
internal static class GroupScopeNavigation
{
    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    /// <summary>
    /// Drills into <paramref name="groupId"/>'s own scope: selects its first child (tagged
    /// <see cref="DesignerFocusIntent.JumpedTo"/>, so a canvas moves real DOM focus there) or,
    /// for a group with no fields yet, a node-less scope selection a canvas reads as "focus the
    /// scope's own heading instead" -- and announces the group is now being edited.
    /// </summary>
    /// <param name="editContext">
    /// The mutation engine to select and announce through.
    /// </param>
    /// <param name="groupId">
    /// The identifier of the <see cref="NodeType.Repeating"/> node to drill into.
    /// </param>
    internal static void Enter(DesignerEditContext editContext, string groupId)
    {
        ArgumentNullException.ThrowIfNull(editContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        var definition = editContext.Draft.Definition;
        var located = DefinitionMutations.FindNodeLocation(definition, groupId);

        if (located is not { } location)
        {
            return;
        }

        var page = definition.Pages[location.PageIndex];
        var section = page.Sections[location.SectionIndex];
        var group = section.Nodes[location.NodeIndex];
        var firstChildId = group.Children.Count > 0 ? group.Children[0].Id : null;

        var selection = firstChildId is not null
            ? DesignerSelection.ForNode(firstChildId, page.Id, section.Id, DesignerFocusIntent.JumpedTo) with { GroupId = groupId }
            : DesignerSelection.ForGroupScope(page.Id, section.Id, groupId, DesignerFocusIntent.JumpedTo);

        editContext.Select(selection);
        editContext.Announce(Localizer["AnnouncementGroupScopeEntered", DescribeNode(group)].Value);
    }

    /// <summary>
    /// Leaves <paramref name="groupId"/>'s own scope: selects the group's own top-level row
    /// (tagged <see cref="DesignerFocusIntent.JumpedTo"/>, so a canvas moves real DOM focus back
    /// to it) and announces the return.
    /// </summary>
    /// <param name="editContext">
    /// The mutation engine to select and announce through.
    /// </param>
    /// <param name="groupId">
    /// The identifier of the <see cref="NodeType.Repeating"/> node whose scope is being left.
    /// </param>
    internal static void Exit(DesignerEditContext editContext, string groupId)
    {
        ArgumentNullException.ThrowIfNull(editContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        var definition = editContext.Draft.Definition;
        var located = DefinitionMutations.FindNodeLocation(definition, groupId);

        if (located is not { } location)
        {
            return;
        }

        var page = definition.Pages[location.PageIndex];
        var section = page.Sections[location.SectionIndex];
        var group = section.Nodes[location.NodeIndex];

        editContext.Select(DesignerSelection.ForNode(groupId, page.Id, section.Id, DesignerFocusIntent.JumpedTo));
        editContext.Announce(Localizer["AnnouncementGroupScopeExited", DescribeNode(group)].Value);
    }

    private static string DescribeNode(FormNode node) =>
        node.Label ?? Localizer["UntitledNodeLabel", Localizer[$"NodeType{node.Type}"].Value].Value;
}
