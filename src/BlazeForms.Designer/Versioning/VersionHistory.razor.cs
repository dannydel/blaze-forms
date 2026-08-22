using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BlazeForms.Hosting;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Versioning;

/// <summary>
/// The version-history panel (PRD §7): every published or retired version of a form, newest
/// first, each carrying its change note, author, date, and submission count, plus a Retire action
/// on a currently-published version and a "revise as draft" action on any past version. Neither
/// action ever mutates the version it acts on — <see cref="OpenRetireConfirmation"/> only ever
/// opens <see cref="RetireConfirmationDialog"/>, which is the one place <see cref="Store"/>'s own
/// <see cref="IFormDefinitionStore.RetireAsync"/> is actually called; <see cref="ReviseAsDraftAsync"/>
/// builds an entirely new, unpublished draft via <see cref="FormLifecycle.ReviseAsDraft"/> rather
/// than touching the version it restores content from (AGENTS.md invariant #3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Newest first.</b> <see cref="IFormDefinitionStore.ListVersionsAsync"/> documents its own
/// return order as oldest first (and <c>InMemoryFormDefinitionStore</c> matches that exactly, an
/// ascending sort by version number) — this panel reverses that once, right after loading, so an
/// author opening version history sees the version most relevant to them (the newest) without
/// scrolling.
/// </para>
/// <para>
/// <b>A fresh instance every open, not a toggle.</b> <see cref="FormDesigner"/> mounts this
/// component only while its own panel is showing, tearing it down entirely on close — the same
/// "no persistent instance to toggle" shape every dialog in this project follows — so
/// <see cref="OnInitializedAsync"/>'s own load is always a genuine, up-to-date fetch: a publish or
/// a retire that happened since this panel was last open (through <see cref="PublishDialog"/>, or
/// through this very panel's own <see cref="RetireConfirmationDialog"/> on a previous open) is
/// never shown stale.
/// </para>
/// <para>
/// <b>Not a modal.</b> Unlike <see cref="PublishDialog"/> and <see cref="RetireConfirmationDialog"/>,
/// this panel is <c>role="region"</c>, not <c>role="dialog" aria-modal="true"</c>, and needs no
/// focus-trap JS module of its own: it is a disclosure panel an author can tab out of into the
/// rest of the designer, not a modal blocking interaction with it.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class VersionHistory : ComponentBase
{
    private IReadOnlyList<FormVersionSummary> _versions = [];
    private bool _isLoading = true;
    private int? _retireTargetVersion;
    private ElementReference _closeButtonElement;
    private bool _revising;

    /// <summary>
    /// The form whose version history this panel loads and acts on.
    /// </summary>
    [Parameter, EditorRequired]
    public string FormId { get; set; } = default!;

    /// <summary>
    /// Where this panel loads version summaries from, retires a version through, and (for
    /// "revise as draft") loads a past version's full content from and saves the resulting new
    /// draft to.
    /// </summary>
    [Parameter, EditorRequired]
    public IFormDefinitionStore Store { get; set; } = default!;

    /// <summary>
    /// Raised once this panel should close — its own Close button, <c>Esc</c>, or a completed
    /// "revise as draft" (<see cref="ReviseAsDraftAsync"/> raises this immediately after
    /// <see cref="OnRevised"/>). Carries no payload; <see cref="FormDesigner"/> owns whether this
    /// panel is showing at all.
    /// </summary>
    [Parameter]
    public EventCallback OnClosed { get; set; }

    /// <summary>
    /// Raised with the new draft <see cref="ReviseAsDraftAsync"/> just saved — <see cref="FormDesigner"/>'s
    /// own hook for tearing down whatever <c>DesignerEditContext</c> it currently owns and
    /// constructing a fresh one over this draft, so the author can edit and publish it as the
    /// form's next version. Never raised by a retire, which changes no draft at all.
    /// </summary>
    [Parameter]
    public EventCallback<FormVersion> OnRevised { get; set; }

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private string TitleId { get; } = "bf-version-history-" + Guid.NewGuid().ToString("n") + "-title";

    /// <summary>
    /// The most recently loaded version list, newest first. <c>internal</c>, not <c>private</c>,
    /// solely so a test can assert the loaded picture without parsing rendered markup.
    /// </summary>
    internal IReadOnlyList<FormVersionSummary> Versions => _versions;

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnInitializedAsync() => await ReloadVersionsAsync();

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await _closeButtonElement.FocusAsync();
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Called only from lifecycle-adjacent methods that must themselves stay on the renderer's synchronization context, which set component state right after this returns.")]
    private async Task ReloadVersionsAsync()
    {
        _isLoading = true;
        var loaded = await Store.ListVersionsAsync(FormId);
        _versions = [.. loaded.Reverse()];
        _isLoading = false;
    }

    private Task CloseAsync() => OnClosed.InvokeAsync();

    private void OpenRetireConfirmation(int version) => _retireTargetVersion = version;

    /// <summary>
    /// Closes <see cref="RetireConfirmationDialog"/> and reloads this panel's own list either
    /// way -- a cancel leaves it unchanged, and a confirmed retire is cheap to just re-fetch
    /// rather than track separately. Focus lands on this panel's own Close button, since the row
    /// action that opened the dialog may no longer be the one this panel renders for that row once
    /// the reload lands (a confirmed retire removes that row's own Retire action entirely).
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Must stay on the renderer's own synchronization context through to the FocusAsync call at the end.")]
    private async Task CloseRetireConfirmationAsync()
    {
        _retireTargetVersion = null;
        await ReloadVersionsAsync();
        await _closeButtonElement.FocusAsync();
    }

    /// <summary>
    /// Restores a past version's content as a brand-new draft (PRD §7's "restoring means
    /// publishing it again as the next version"): loads that version's full content (the summary
    /// this panel already holds carries no <c>FormDefinition</c>), hands it to
    /// <see cref="FormLifecycle.ReviseAsDraft"/> — which returns a new value and leaves the version
    /// it read from completely untouched (AGENTS.md invariant #3) — saves the result as the form's
    /// one working draft, then raises <see cref="OnRevised"/> so <see cref="FormDesigner"/> can
    /// swap its own editing session onto it, and finally closes this panel.
    /// </summary>
    /// <remarks>
    /// <b>Re-entry guard.</b> Under an async host store, both <c>await</c>s below leave this click
    /// handler suspended for at least one render cycle with every row's "Revise as draft" button
    /// still in the DOM — a fast double-click on the same row, or a click on a second row while
    /// the first is still in flight, must not save a second draft over the one the first call is
    /// still building (there is only ever one working draft per form). <see cref="_revising"/> is
    /// set synchronously, as the very first statement, so a second call made while the first is
    /// still executing observes it already set and returns immediately; every row's own "Revise as
    /// draft" button disables the instant it starts, for the same "guard the flag, not just the
    /// button" reason <see cref="PublishDialog.ConfirmAsync"/> documents for itself. It is cleared
    /// in the <c>finally</c> below only when this call did NOT reach a successful revise, since a
    /// successful revise raises <see cref="OnClosed"/> and this instance is torn down regardless.
    /// <c>internal</c>, not <c>private</c>, solely so a test can call it directly and prove the
    /// guard deterministically.
    /// </remarks>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "An event-callback handler resumes on the renderer's synchronization context, and must stay on it through to the OnClosed.InvokeAsync call at the end.")]
    internal async Task ReviseAsDraftAsync(int version)
    {
        if (_revising)
        {
            return;
        }

        _revising = true;
        var succeeded = false;

        try
        {
            var fullVersion = await Store.GetVersionAsync(FormId, version)
                ?? throw new InvalidOperationException($"No version {version} was found for form '{FormId}'.");

            var draft = FormLifecycle.ReviseAsDraft(fullVersion);
            await Store.SaveDraftAsync(draft);
            succeeded = true;

            await OnRevised.InvokeAsync(draft);
            await OnClosed.InvokeAsync();
        }
        finally
        {
            if (!succeeded)
            {
                _revising = false;
            }
        }
    }

    private static string StateLabel(FormLifecycleState state) => state switch
    {
        FormLifecycleState.Published => Localizer["VersionHistoryStatePublished"].Value,
        FormLifecycleState.Retired => Localizer["VersionHistoryStateRetired"].Value,
        _ => state.ToString(),
    };

    private static string StateModifier(FormLifecycleState state) => state switch
    {
        FormLifecycleState.Published => "published",
        FormLifecycleState.Retired => "retired",
        _ => "draft",
    };

    private static string FormatDate(FormVersionSummary summary)
    {
        var published = summary.PublishedAt is { } publishedAt
            ? Localizer["VersionHistoryPublishedOn", FormatInstant(publishedAt)].Value
            : null;
        var retired = summary.RetiredAt is { } retiredAt
            ? Localizer["VersionHistoryRetiredOn", FormatInstant(retiredAt)].Value
            : null;

        return (published, retired) switch
        {
            (not null, not null) => $"{published} · {retired}",
            (not null, null) => published,
            (null, not null) => retired,
            (null, null) => string.Empty,
        };
    }

    private static string FormatInstant(DateTimeOffset instant) =>
        instant.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
}
