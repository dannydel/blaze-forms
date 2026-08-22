using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using BlazeForms.Hosting;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace BlazeForms.Versioning;

/// <summary>
/// The retire confirm (PRD §7): "retiring stops new fills; existing submissions remain
/// renderable", with no unpublish and no un-retire once confirmed. <see cref="VersionHistory"/>
/// opens this only for a version currently in the <see cref="FormLifecycleState.Published"/>
/// state and only ever calls its own <see cref="IFormDefinitionStore.RetireAsync"/> through this
/// dialog's "Retire" action — never directly.
/// </summary>
/// <remarks>
/// <para>
/// <b>A fresh instance every open.</b> <see cref="VersionHistory"/> mounts this component only
/// while its own dialog is showing, tearing it down entirely on close — the same "no persistent
/// instance to toggle" shape every other dialog in this project follows.
/// </para>
/// <para>
/// <b>Focus trap.</b> This dialog is <c>role="dialog" aria-modal="true"</c>, labelled by its own
/// title, and moves real DOM focus to its Cancel button (not the destructive "Retire" button) the
/// moment its first render lands — the same "default to the safe action" rationale
/// <c>DeleteProtectionDialog</c> documents for itself: a stray <c>Enter</c> reaching whichever
/// control already has focus must never itself retire anything. The collocated
/// <c>RetireConfirmationDialog.razor.js</c> module cycles <c>Tab</c>/<c>Shift+Tab</c> between this
/// dialog's two buttons, the same genuine-platform-gap rationale every other trapped dialog in
/// this project documents for its own trap. <c>Escape</c> needs no JS at all.
/// </para>
/// <para>
/// <b>Focus destination after closing.</b> Both <see cref="ConfirmAsync"/> and
/// <see cref="CancelAsync"/> only ever raise <see cref="OnClosed"/> — moving real DOM focus back
/// once this dialog leaves the DOM is <see cref="VersionHistory"/>'s own job, the same split
/// every other dialog in this project documents for itself.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class RetireConfirmationDialog : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The static web asset path this component imports its focus-trap JS module from, following
    /// the same <c>_content/{assembly}/{path}</c> convention every collocated Razor Class Library
    /// JS file resolves to. <c>internal</c> so a test can set up the module mock against the exact
    /// path this component requests.
    /// </summary>
    internal const string ModulePath = "./_content/BlazeForms.Designer/Versioning/RetireConfirmationDialog.razor.js";

    private readonly string _instanceId = "bf-retire-dialog-" + Guid.NewGuid().ToString("n");
    private ElementReference _dialogElement;
    private ElementReference _cancelButtonElement;
    private IJSObjectReference? _module;
    private IJSObjectReference? _focusTrapHandle;
    private bool _disposed;
    private bool _retiring;

    /// <summary>
    /// The form whose version is being retired.
    /// </summary>
    [Parameter, EditorRequired]
    public string FormId { get; set; } = default!;

    /// <summary>
    /// Where this dialog retires the version through.
    /// </summary>
    [Parameter, EditorRequired]
    public IFormDefinitionStore Store { get; set; } = default!;

    /// <summary>
    /// The published version number this dialog is asking about.
    /// </summary>
    [Parameter, EditorRequired]
    public int Version { get; set; }

    /// <summary>
    /// Raised once this dialog should close — after a confirmed retire (<see cref="ConfirmAsync"/>)
    /// or a cancel (<see cref="CancelAsync"/>). Carries no payload; <see cref="VersionHistory"/>
    /// reloads its own list either way, since a cancel leaves it unchanged and a confirm is cheap
    /// to just re-fetch rather than track separately.
    /// </summary>
    [Parameter]
    public EventCallback OnClosed { get; set; }

    /// <summary>
    /// Used only to import <see cref="ModulePath"/>'s focus-trap module.
    /// </summary>
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private string TitleId => _instanceId + "-title";

    private string BodyId => _instanceId + "-body";

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
        _focusTrapHandle = await _module.InvokeAsync<IJSObjectReference>("attachFocusTrap", _dialogElement);
        await _cancelButtonElement.FocusAsync();
    }

    /// <summary>
    /// Detaches the focus-trap listener and disposes the imported module. Safe to call more than
    /// once.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Blazor disposes a component on its own renderer's synchronization context, same as every other lifecycle method in this file.")]
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_focusTrapHandle is not null)
        {
            await _focusTrapHandle.InvokeVoidAsync("dispose");
            await _focusTrapHandle.DisposeAsync();
            _focusTrapHandle = null;
        }

        if (_module is not null)
        {
            await _module.DisposeAsync();
            _module = null;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Whether the focus-trap JS module has been imported. <c>internal</c>, not <c>private</c>,
    /// solely so a test can prove <see cref="DisposeAsync"/> actually disposes it — the same
    /// rationale every other trapped dialog in this project gives for itself.
    /// </summary>
    internal bool HasImportedModule => _module is not null;

    /// <summary>
    /// Retires the version through <see cref="Store"/>, then closes. There is no unpublish and no
    /// un-retire (PRD §7); this dialog's own "Retire" action is the one and only irreversible step.
    /// </summary>
    /// <remarks>
    /// <b>Re-entry guard.</b> Under an async host store, the single <c>await</c> below leaves this
    /// click handler suspended for at least one render cycle with the "Retire" button still in the
    /// DOM — a fast double-click, or any re-entrant call, must not call
    /// <see cref="IFormDefinitionStore.RetireAsync"/> a second time on a version <see cref="Store"/>
    /// has already retired (its own non-<see cref="FormLifecycleState.Published"/> guard rejects
    /// that outright). <see cref="_retiring"/> is set synchronously, as the very first statement,
    /// so a second call made while the first is still executing observes it already set and
    /// returns immediately; the "Retire" button's own <c>disabled</c> attribute reflects it too,
    /// for the same "guard the flag, not just the button" reason
    /// <see cref="PublishDialog.ConfirmAsync"/> documents for itself. It is cleared in the
    /// <c>finally</c> below only when this call did NOT reach a successful retire,
    /// since a successful retire raises <see cref="OnClosed"/> and this instance is torn down
    /// regardless. <c>internal</c>, not <c>private</c>, solely so a test can call it directly and
    /// prove the guard deterministically.
    /// </remarks>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "An event-callback handler resumes on the renderer's synchronization context, and must stay on it through to the OnClosed.InvokeAsync call at the end.")]
    internal async Task ConfirmAsync()
    {
        if (_retiring)
        {
            return;
        }

        _retiring = true;
        var succeeded = false;

        try
        {
            await Store.RetireAsync(FormId, Version);
            succeeded = true;
            await OnClosed.InvokeAsync();
        }
        finally
        {
            if (!succeeded)
            {
                _retiring = false;
            }
        }
    }

    /// <summary>
    /// Cancels — the <c>Esc</c> path, and the visible Cancel button's — without touching
    /// <see cref="Store"/> at all, then closes.
    /// </summary>
    private Task CancelAsync() => OnClosed.InvokeAsync();

    private Task OnDialogKeyDown(KeyboardEventArgs e) =>
        string.Equals(e.Key, "Escape", StringComparison.Ordinal) ? CancelAsync() : Task.CompletedTask;
}
