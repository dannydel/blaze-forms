using BlazeForms.Definitions;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Palette;

/// <summary>
/// A searchable, grouped catalogue of node types a form author can add to the canvas (PRD §4.1,
/// §5). Groups follow the table in PRD §5: Text, Numeric, Date, Choice, and Static hold their
/// P1 types; Advanced holds the schema-only <see cref="NodeType.Calc"/>, the addable
/// <see cref="NodeType.Repeating"/>, and every type <see cref="FormSchema.IsReservedForLaterPhase"/>
/// reports, so a future reserved type shows up here, disabled, without a change to this file.
/// <see cref="NodeType.Repeating"/> is named explicitly rather than folded into
/// <see cref="FormSchema.ReservedNodeTypes"/> because it is no longer reserved
/// (repeating-groups-plan.md, Increment C) but still belongs in Advanced alongside Calc, not in
/// one of the P1 type-table groups above.
/// </summary>
/// <remarks>
/// A reserved type renders as a disabled entry carrying a "Phase 2" badge, is marked
/// <c>aria-disabled</c>, and — because it renders as a native <c>disabled</c> button — is removed
/// from the tab sequence and cannot raise <see cref="OnAddRequested"/>, exactly the disabled
/// affordance the reference design uses (PRD §5). Selecting an addable entry raises
/// <see cref="OnAddRequested"/>; nothing in this phase wires that through to a canvas mutation
/// yet (that lands in Phase 3), so it is safe to raise from any consumer today without side
/// effects. Typing in the search box only ever re-renders this component — it never touches a
/// parameter a parent observes — which is what keeps the rest of the designer shell untouched
/// while an author searches (AGENTS.md render-discipline standard).
/// </remarks>
public partial class FieldPalette : ComponentBase
{
    private sealed record PaletteGroup(string ResourceKey, IReadOnlyList<NodeType> Types);

    private static readonly PaletteGroup[] Groups =
    [
        new("PaletteGroupText", [NodeType.Text, NodeType.TextArea, NodeType.Email, NodeType.Phone]),
        new("PaletteGroupNumeric", [NodeType.Number, NodeType.Currency]),
        new("PaletteGroupDate", [NodeType.Date, NodeType.DateRange]),
        new("PaletteGroupChoice", [NodeType.Select, NodeType.Radio, NodeType.CheckboxGroup, NodeType.YesNo, NodeType.Boolean]),
        new("PaletteGroupStatic", [NodeType.Heading, NodeType.Paragraph, NodeType.Callout, NodeType.Divider]),
        new("PaletteGroupAdvanced", [NodeType.Calc, NodeType.Repeating, .. FormSchema.ReservedNodeTypes]),
    ];

    private readonly string _searchInputId = "bf-palette-search-" + Guid.NewGuid().ToString("n");
    private string _searchTerm = string.Empty;

    /// <summary>
    /// Raised when the author selects an addable (non-reserved) entry. Phase 1 declares and
    /// raises this so the parameter contract is in place; the real add-to-canvas mutation lands
    /// in Phase 3.
    /// </summary>
    [Parameter]
    public EventCallback<NodeType> OnAddRequested { get; set; }

    /// <summary>
    /// Whether the canvas is currently scoped inside a repeating group's own <c>Children</c>
    /// (repeating-groups-plan.md, Increment C's "one nesting level" rule). Hides
    /// <see cref="NodeType.Repeating"/> from the Advanced group entirely while
    /// <see langword="true"/> -- the palette's own half of the "no repeating inside a repeating"
    /// guard <c>DefinitionMutations.InsertChildNode</c> also defensively enforces -- so an author
    /// scoped into a group's fields is never offered an entry that would only ever throw.
    /// </summary>
    [Parameter]
    public bool IsInsideRepeatingGroup { get; set; }

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private IEnumerable<PaletteGroup> VisibleGroups => Groups
        .Select(group => new PaletteGroup(group.ResourceKey, [.. group.Types.Where(MatchesSearch).Where(IsOfferable)]))
        .Where(group => group.Types.Count > 0);

    private bool IsOfferable(NodeType nodeType) => !IsInsideRepeatingGroup || nodeType != NodeType.Repeating;

    private static string GroupLabel(string resourceKey) => Localizer[resourceKey].Value;

    private static string TypeLabel(NodeType nodeType) => Localizer[$"NodeType{nodeType}"].Value;

    private bool MatchesSearch(NodeType nodeType) =>
        string.IsNullOrWhiteSpace(_searchTerm)
        || TypeLabel(nodeType).Contains(_searchTerm, StringComparison.OrdinalIgnoreCase);

    private void OnSearchInput(ChangeEventArgs args) => _searchTerm = args.Value?.ToString() ?? string.Empty;

    private Task AddAsync(NodeType nodeType) => OnAddRequested.InvokeAsync(nodeType);
}
