using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using BlazeForms.Designer;
using BlazeForms.Hosting;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Preview;

/// <summary>
/// The designer's live preview surface (PRD §4.1, §11): renders the current working draft through
/// the real <see cref="BlazeForms.FormRenderer"/> -- live conditional logic, live validation, its
/// own step navigation -- but with <see cref="BlazeForms.FormRenderer.Ephemeral"/> set, so nothing
/// about a preview fill ever reaches a host's registered <c>IFormSubmissionSink</c> or
/// <c>IFormDraftStore</c>: no submission is ever sent, and no draft is ever loaded, autosaved, or
/// deleted. Test data lives only inside this pane's own throwaway <see cref="BlazeForms.FormRenderer"/>
/// instance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read-through, not an editing surface.</b> <see cref="EditContext"/>'s own
/// <see cref="DesignerEditContext.Draft"/> is handed to <see cref="BlazeForms.FormRenderer.Version"/>
/// as-is -- the renderer does not require <see cref="Versioning.FormLifecycleState.Published"/>, so
/// an unpublished working draft previews exactly as authored. This pane never calls any
/// <see cref="DesignerEditContext"/> mutation method; the draft it previews is unchanged by
/// whatever the respondent-side fill inside it does. <c>RespondentKey</c> is left at its default
/// <see langword="null"/> -- a preview fill is always anonymous, on top of
/// <see cref="BlazeForms.FormRenderer.Ephemeral"/> already suppressing the draft store outright.
/// </para>
/// <para>
/// <b>Test data is discarded on exit, for free.</b> <c>FormDesigner</c> only ever mounts this
/// component behind an <c>@if</c> (see its own remarks) -- leaving preview tears this whole
/// component, and the <see cref="BlazeForms.FormRenderer"/> it hosts, out of the render tree
/// entirely, and re-entering constructs a brand-new instance of both. Nothing here needs its own
/// reset logic; there is no state left to reset.
/// </para>
/// <para>
/// <b>Focus management.</b> Moves real DOM focus to this pane's own heading the moment its first
/// render lands (PRD §11) -- <c>FormDesigner</c>'s toolbar toggle is what restores focus back to
/// itself once this pane leaves the DOM, the same split every designer dialog's own
/// open/close pair uses.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class PreviewPane : ComponentBase
{
    private readonly string _instanceId = "bf-preview-" + Guid.NewGuid().ToString("n");
    private ElementReference _headingElement;

    /// <summary>
    /// The mutation engine whose current <see cref="DesignerEditContext.Draft"/> this pane
    /// previews. Never mutated by this pane.
    /// </summary>
    [Parameter, EditorRequired]
    public DesignerEditContext EditContext { get; set; } = default!;

    /// <summary>
    /// Raised when the author asks to leave preview -- this pane's own Exit button.
    /// <c>FormDesigner</c> owns whether this pane is mounted at all; this carries no payload for
    /// the same reason every other designer dialog's own close callback does not.
    /// </summary>
    [Parameter]
    public EventCallback OnExit { get; set; }

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private string HeadingId => _instanceId + "-heading";

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await _headingElement.FocusAsync();
        }
    }

    /// <summary>
    /// The ephemeral fill's own <see cref="BlazeForms.FormRenderer.OnSubmitted"/> hook.
    /// Deliberately does nothing beyond letting the renderer's own default confirmation (or a
    /// future custom <c>ConfirmationTemplate</c>) show — a preview submission has nowhere else to
    /// go, and PRD §4.1 asks for exactly that: test data discarded, with no host side effect, ever.
    /// </summary>
    private static Task OnPreviewSubmitted(FormSubmissionEnvelope envelope) => Task.CompletedTask;

    private Task ExitAsync() => OnExit.InvokeAsync();
}
