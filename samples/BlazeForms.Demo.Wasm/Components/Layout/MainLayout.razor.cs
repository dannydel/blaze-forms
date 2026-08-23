using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazeForms.Demo.Wasm.Components.Layout;

/// <summary>
/// Code-behind for the demo's shared page shell — owns the high-contrast theme toggle's state
/// (docs/accessibility-statement-plan.md, Increment C4) declaratively, the same way any other
/// Blazor component owns its own UI state, rather than letting a JS module mutate the button's
/// <c>aria-pressed</c> attribute and label behind Blazor's back (where the first unrelated
/// conditional edit to this markup would silently revert both, with nothing to notice).
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "The generated partial class from MainLayout.razor's @inherits LayoutComponentBase is public by Blazor's own component convention -- this code-behind half has to match it.")]
public sealed partial class MainLayout : IAsyncDisposable
{
    /// <summary>
    /// The static web asset path this component imports its theme-toggle JS module from,
    /// following the same <c>./{path}</c> convention <c>samples/BlazeForms.Sample</c>'s own
    /// collocated JS resolves to for the app's own project (not the <c>_content/{assembly}/</c>
    /// form a Razor Class Library's static web assets use). <c>internal</c> so a test can set up
    /// a module mock against the exact path this component requests.
    /// </summary>
    internal const string ModulePath = "./Components/Layout/MainLayout.razor.js";

    private IJSObjectReference? _module;
    private bool _isHighContrast;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
        }
    }

    /// <summary>
    /// Flips <see cref="_isHighContrast"/> — which the markup's own <c>aria-pressed</c> binding
    /// re-renders from declaratively — then asks the imported module to apply (or remove) the
    /// <c>data-bf-theme="high-contrast"</c> attribute on <c>&lt;html&gt;</c>, the one piece of
    /// this toggle's state that lives outside Blazor's component tree. Null-guarded: a click
    /// landing in the brief window before <see cref="OnAfterRenderAsync"/>'s first-render import
    /// has completed still updates the button's own visible/announced state, it just has nothing
    /// to call yet — the next click (by then always past that window) catches the theme up.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "An @onclick event callback must resume on the renderer's synchronization context, not a captured-context-free one, so Blazor re-renders the button's aria-pressed binding after this method returns.")]
    private async Task ToggleHighContrastAsync()
    {
        _isHighContrast = !_isHighContrast;

        if (_module is not null)
        {
            await _module.InvokeVoidAsync("setHighContrastTheme", _isHighContrast);
        }
    }

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Blazor disposes a component on its own renderer's synchronization context, same as every other lifecycle method in this file.")]
    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }
}
