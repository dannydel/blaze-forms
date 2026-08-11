using BlazeForms.Designer;
using Microsoft.AspNetCore.Components;

namespace BlazeForms;

/// <summary>
/// The always-present <c>role="status" aria-live="polite"</c> region that speaks every
/// <see cref="DesignerEditContext.Announced"/> message as plain language (PRD §4.1, §11 --
/// e.g. "Moved to position 3 of 5 in 'Transportation'."). Visually hidden, same as
/// <see cref="FormRenderer"/>'s own step-change live region, since the announcement duplicates
/// what a sighted author already sees change on the canvas.
/// </summary>
/// <remarks>
/// Subscribes to <see cref="EditContext"/>'s <see cref="DesignerEditContext.Announced"/> event, so
/// it implements <see cref="IAsyncDisposable"/> and unsubscribes in <see cref="DisposeAsync"/>
/// (AGENTS.md Blazor standards) -- resubscribing instead of leaking a handler on a discarded
/// context if <see cref="EditContext"/> itself is ever replaced across renders, even though
/// <see cref="FormDesigner"/> only ever constructs one per instance today.
/// </remarks>
public partial class AriaLiveRegion : ComponentBase, IAsyncDisposable
{
    // Zero-width space (U+200B): no glyph, and screen readers do not speak it. Appending it on
    // every other announcement forces the region's rendered text to always differ from what it
    // just was, even when two consecutive DesignerAnnouncement.Message values are byte-for-byte
    // identical (e.g. duplicating the same node twice in a row) -- a role="status" region is only
    // spoken again once its text content actually changes, so without this the second of two
    // identical announcements would go unheard.
    private const string DuplicateMessageMarker = "​";

    private string _message = string.Empty;
    private AriaLivePoliteness _politeness = AriaLivePoliteness.Polite;
    private DesignerEditContext? _subscribedContext;
    private bool _markerOnNextMessage;
    private bool _disposed;

    /// <summary>
    /// The context whose announcements this region speaks.
    /// </summary>
    [Parameter, EditorRequired]
    public DesignerEditContext EditContext { get; set; } = default!;

    private string AriaLive => _politeness == AriaLivePoliteness.Assertive ? "assertive" : "polite";

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_subscribedContext, EditContext))
        {
            return;
        }

        if (_subscribedContext is not null)
        {
            _subscribedContext.Announced -= OnAnnounced;
        }

        EditContext.Announced += OnAnnounced;
        _subscribedContext = EditContext;
    }

    private void OnAnnounced(DesignerAnnouncement announcement)
    {
        _markerOnNextMessage = !_markerOnNextMessage;
        _message = _markerOnNextMessage ? announcement.Message + DuplicateMessageMarker : announcement.Message;
        _politeness = announcement.Politeness;
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Unsubscribes from <see cref="_subscribedContext"/>. Safe to call more than once; does not
    /// dispose <see cref="EditContext"/> itself -- that remains its owner's (<see cref="FormDesigner"/>'s)
    /// job.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        if (_subscribedContext is not null)
        {
            _subscribedContext.Announced -= OnAnnounced;
            _subscribedContext = null;
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
