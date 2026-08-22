using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Designer;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Properties;

/// <summary>
/// Edits a choice node's <see cref="FormNode.Options"/> as a keyed row list (PRD §4.1) — the
/// options editor <see cref="PropertiesPanel"/> hosts for <c>select</c>, <c>radio</c>,
/// <c>checkboxgroup</c>, and <c>yesno</c> nodes (PRD §5). Renders one row per option — a label
/// input plus remove/move-up/move-down controls — and an add-option button; on every committed
/// change it rebuilds the complete replacement list in row order and hands it back through
/// <see cref="OptionsChanged"/> for <see cref="PropertiesPanel"/> to route through
/// <see cref="DesignerEditContext.UpdateNode"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stable values (AGENTS.md invariant #5).</b> A bare "one label per line" textarea has only a
/// line's position to key an option by, so removing or reordering a line silently reassigns every
/// later line's stored value to whatever option now sits at its old position — corrupting any
/// visibility rule or captured submission that named an option by its (now misattributed)
/// <see cref="FormOption.Value"/>. This editor fixes that by keying every row by its own value
/// instead of its position: <see cref="_rows"/> holds this component's own working list (value
/// plus label, one entry per row), seeded from <see cref="Options"/> whenever a genuinely new,
/// externally-produced list arrives — an initial render, an undo/redo, or a sibling control's own
/// commit — but never rebuilt merely because this editor's own previous commit has echoed back
/// through the same parameter (see <see cref="OnParametersSet"/>'s reference check). A row's value
/// is minted exactly once, by <see cref="AddRowAsync"/>, opaque and never derived from the label,
/// and from that point on follows the row through every label edit, every reorder, and the
/// removal of any <em>other</em> row: removing a row drops exactly that row's value, and every
/// surviving row keeps its own, whatever position it now occupies.
/// </para>
/// <para>
/// <b>Commit semantics.</b> Every mutating action — a label edit (on <c>change</c>, i.e. blur,
/// never keystroke), add, remove, or move — rebuilds the full replacement list from
/// <see cref="_rows"/> in row order and raises <see cref="OptionsChanged"/> exactly once, the same
/// one-commit-per-edit discipline every other control in <see cref="PropertiesPanel"/> follows
/// (AGENTS.md render discipline; PRD §4.1's depth-50 undo stack would otherwise flood on every
/// keystroke or, here, every reorder step).
/// </para>
/// <para>
/// <b>Focus.</b> Adding a row moves focus to its own new label input; removing a row moves focus
/// to the neighbour row that slides into its old position (or, once it was the last row, the row
/// now last) or, when no row survives, to the add button — the same one-shot
/// "focus-on-next-render" flag pattern <see cref="Canvas.CanvasNodeRow"/> and
/// <see cref="PropertiesPanel"/> both use for their own post-mutation focus moves. A reorder never
/// requests focus of its own: the row's move button stays in the DOM under the same
/// <c>@@key</c> — only its position among its siblings changes — so the click that triggered the
/// move has already left native DOM focus exactly where it belongs.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class OptionsEditor : ComponentBase
{
    private readonly string _instanceId = "bf-options-editor-" + Guid.NewGuid().ToString("n");
    private readonly List<OptionRow> _rows = [];
    private IReadOnlyList<FormOption>? _lastReflectedOptions;
    private ElementReference _addButtonElement;
    private string? _focusRowValueOnNextRender;
    private bool _focusAddButtonOnNextRender;

    /// <summary>
    /// The node's current options, in display order. This editor holds its own per-row working
    /// state (see this type's own remarks); <see cref="OptionsChanged"/> is the only way a change
    /// reaches its owner.
    /// </summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<FormOption> Options { get; set; } = default!;

    /// <summary>
    /// Raised once per commit (a label blur, an add, a remove, or a move — never a keystroke)
    /// with the complete replacement list — <see cref="PropertiesPanel"/>'s only intended
    /// subscriber routes this straight into <see cref="DesignerEditContext.UpdateNode"/>.
    /// </summary>
    [Parameter]
    public EventCallback<IReadOnlyList<FormOption>> OptionsChanged { get; set; }

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_lastReflectedOptions, Options))
        {
            // Options is the exact list this editor itself just committed (CommitAsync stamps
            // _lastReflectedOptions with the very instance it raises) -- PropertiesPanel routes
            // it straight through DesignerEditContext.UpdateNode without copying it, so re-seeding
            // here would discard nothing real but would still needlessly recreate every row (and,
            // with it, every row's captured ElementReference) on a render this editor caused.
            return;
        }

        _lastReflectedOptions = Options;
        _rows.Clear();
        _rows.AddRange(Options.Select(option => new OptionRow(option.Value, option.Label)));
    }

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusRowValueOnNextRender is { } value)
        {
            _focusRowValueOnNextRender = null;
            var row = _rows.Find(candidate => string.Equals(candidate.Value, value, StringComparison.Ordinal));

            if (row is not null)
            {
                await row.Input.FocusAsync();
            }
        }
        else if (_focusAddButtonOnNextRender)
        {
            _focusAddButtonOnNextRender = false;
            await _addButtonElement.FocusAsync();
        }
    }

    private string RowInputId(OptionRow row) => string.Concat(_instanceId, "-", row.Value);

    private Task CommitLabelEditAsync(OptionRow row, ChangeEventArgs e)
    {
        row.Label = e.Value?.ToString() ?? string.Empty;
        return CommitAsync();
    }

    /// <summary>
    /// Appends a new row with a freshly minted, opaque, stable value — never derived from the
    /// label, the same reason <see cref="FormIds"/> never derives an identifier from
    /// author-supplied text (AGENTS.md invariant #5) — and asks that row's own input to take focus
    /// once this render lands.
    /// </summary>
    private Task AddRowAsync()
    {
        var row = new OptionRow(NewOptionValue(), string.Empty);
        _rows.Add(row);
        _focusRowValueOnNextRender = row.Value;
        return CommitAsync();
    }

    /// <summary>
    /// Drops exactly <paramref name="row"/>'s value — every surviving row keeps its own — and
    /// moves focus to whichever row slides into its old position, or to the add button once no
    /// row survives.
    /// </summary>
    private Task RemoveRowAsync(OptionRow row)
    {
        var index = _rows.IndexOf(row);

        if (index < 0)
        {
            return Task.CompletedTask;
        }

        _rows.RemoveAt(index);

        if (_rows.Count > 0)
        {
            _focusRowValueOnNextRender = _rows[Math.Min(index, _rows.Count - 1)].Value;
        }
        else
        {
            _focusAddButtonOnNextRender = true;
        }

        return CommitAsync();
    }

    /// <summary>
    /// Moves <paramref name="row"/> by <paramref name="delta"/> positions (<c>-1</c> up, <c>+1</c>
    /// down) — a no-op past either end, so the disabled boundary buttons in the markup are a
    /// backstop, not the only guard. <paramref name="row"/> keeps its own value; only its position
    /// in <see cref="_rows"/> changes.
    /// </summary>
    private Task MoveRowAsync(OptionRow row, int delta)
    {
        var index = _rows.IndexOf(row);
        var newIndex = index + delta;

        if (index < 0 || newIndex < 0 || newIndex >= _rows.Count)
        {
            return Task.CompletedTask;
        }

        _rows.RemoveAt(index);
        _rows.Insert(newIndex, row);
        return CommitAsync();
    }

    /// <summary>
    /// Rebuilds the complete replacement list from <see cref="_rows"/>, in row order, and raises
    /// <see cref="OptionsChanged"/> with it — see this type's own remarks for why the snapshot is
    /// also stamped onto <see cref="_lastReflectedOptions"/> before it is raised.
    /// </summary>
    private Task CommitAsync()
    {
        var snapshot = _rows.Select(row => new FormOption { Value = row.Value, Label = row.Label }).ToList();
        _lastReflectedOptions = snapshot;
        return OptionsChanged.InvokeAsync(snapshot);
    }

    private static string NewOptionValue() => "opt-" + Guid.NewGuid().ToString("n");

    /// <summary>
    /// One option row's working state. <see cref="Value"/> is minted once, by
    /// <see cref="AddRowAsync"/>, and never changes; <see cref="Label"/> mutates in place as the
    /// row's own input commits; <see cref="Input"/> captures the row's own input element for the
    /// one-shot focus moves <see cref="AddRowAsync"/>/<see cref="RemoveRowAsync"/> request.
    /// </summary>
    private sealed class OptionRow(string value, string label)
    {
        public string Value { get; } = value;

        public string Label { get; set; } = label;

        public ElementReference Input { get; set; }
    }
}
