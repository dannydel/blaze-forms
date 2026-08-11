using System.Diagnostics.CodeAnalysis;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace BlazeForms;

/// <summary>
/// The in-app keyboard-command reference dialog PRD §4.1 requires ("discoverable via an in-app
/// dialog"): a focus-trapped, labelled dialog listing every canvas keyboard command this slice
/// ships, so an author who never touches a mouse still has a way to learn them all. Purely
/// informational -- it carries no <c>DesignerEditContext</c> parameter and never mutates anything;
/// <see cref="FormDesigner"/> opens it from its own labelled Help button.
/// </summary>
/// <remarks>
/// <b>Focus trap.</b> This dialog is <c>role="dialog" aria-modal="true"</c>, labelled by its own
/// title, and moves real DOM focus to its Close button the moment its first render lands. The
/// collocated <c>KeyboardHelpDialog.razor.js</c> module cycles <c>Tab</c>/<c>Shift+Tab</c> among
/// this dialog's own focusable controls -- the same genuine-platform-gap rationale
/// <c>MoveToPositionDialog.razor.js</c> and <c>DeleteProtectionDialog.razor.js</c> both document
/// for their own traps. <c>Escape</c> needs no JS at all, the same as those two dialogs'.
/// </remarks>
public partial class KeyboardHelpDialog : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The static web asset path this component imports its focus-trap JS module from, following
    /// the same <c>_content/{assembly}/{path}</c> convention every collocated Razor Class Library
    /// JS file resolves to. <c>internal</c> so a test can set up the module mock against the exact
    /// path this component requests.
    /// </summary>
    internal const string ModulePath = "./_content/BlazeForms.Designer/KeyboardHelpDialog.razor.js";

    private readonly string _instanceId = "bf-keyboard-help-" + Guid.NewGuid().ToString("n");
    private ElementReference _dialogElement;
    private ElementReference _closeButtonElement;
    private IJSObjectReference? _module;
    private IJSObjectReference? _focusTrapHandle;
    private bool _disposed;

    /// <summary>
    /// Raised once this dialog should close -- the Close button or <c>Esc</c>. Carries no payload;
    /// the caller (<see cref="FormDesigner"/>) owns whether it is showing at all.
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
        await _closeButtonElement.FocusAsync();
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
    /// solely so a test can prove <see cref="DisposeAsync"/> actually disposes it -- the same
    /// rationale <c>MoveToPositionDialog.HasImportedModule</c> gives for itself.
    /// </summary>
    internal bool HasImportedModule => _module is not null;

    private Task CloseAsync() => OnClosed.InvokeAsync();

    private Task OnDialogKeyDown(KeyboardEventArgs e) =>
        string.Equals(e.Key, "Escape", StringComparison.Ordinal) ? CloseAsync() : Task.CompletedTask;
}
