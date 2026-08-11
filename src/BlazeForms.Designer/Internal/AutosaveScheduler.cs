using System.Diagnostics.CodeAnalysis;
using BlazeForms.Hosting;
using BlazeForms.Versioning;

namespace BlazeForms.Designer.Internal;

/// <summary>
/// Debounced persistence for a <see cref="DesignerEditContext"/>'s draft. The very first save a
/// scheduler is ever asked for fires immediately -- this is what makes "a draft is persisted on
/// the first edit, not on mere open" (PRD §7, <see cref="FormDesigner"/>'s remarks on
/// <c>LoadDraftAsync</c>) actually true. Every save after that waits for
/// <see cref="_debounceInterval"/> of quiet before writing, and a save still pending when a newer
/// one is scheduled is superseded -- its write never happens -- so a burst of rapid mutations
/// (e.g. holding <c>Alt+↓</c>) always coalesces into one write for the last of them.
/// </summary>
internal sealed class AutosaveScheduler : IAsyncDisposable
{
    private readonly IFormDefinitionStore _store;
    private readonly TimeSpan _debounceInterval;
    private readonly Action<Exception>? _onFailure;
    private readonly CancellationTokenSource _disposalCts;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _pendingCts;
    private Task _pendingSave = Task.CompletedTask;
    private bool _hasScheduledOnce;
    private bool _disposed;

    /// <summary>
    /// Creates a scheduler bound to one draft's store.
    /// </summary>
    /// <param name="store">
    /// Where <see cref="ScheduleSave"/> eventually persists the draft.
    /// </param>
    /// <param name="debounceInterval">
    /// How long a save after the first waits for mutations to stop arriving before it actually
    /// writes. <see langword="null"/> selects a 500 ms default.
    /// </param>
    /// <param name="onFailure">
    /// Invoked when a save actually fails -- anything <see cref="IFormDefinitionStore.SaveDraftAsync"/>
    /// throws other than an ordinary superseded-by-a-newer-edit or disposal cancellation. Never
    /// invoked for that ordinary cancellation, which is not a failure. A save attempt failing
    /// this way never faults <see cref="_pendingSave"/> itself -- an unobserved fault there would
    /// go silent the moment a later <see cref="ScheduleSave"/> overwrites it, and would also
    /// rethrow out of <see cref="DisposeAsync"/> during teardown -- so this is the only way such a
    /// failure is ever surfaced. <see langword="null"/> means failures are simply dropped, which
    /// is only appropriate for tests that do not care.
    /// </param>
    /// <param name="externalCancellation">
    /// A token the owning <see cref="DesignerEditContext"/>'s own owner can cancel to stop
    /// autosaving without waiting for <see cref="DisposeAsync"/> to be called. Last, per CA1068,
    /// even though it predates <paramref name="onFailure"/> in this type's own history.
    /// </param>
    internal AutosaveScheduler(
        IFormDefinitionStore store,
        TimeSpan? debounceInterval = null,
        Action<Exception>? onFailure = null,
        CancellationToken externalCancellation = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _debounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(500);
        _onFailure = onFailure;
        _disposalCts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
    }

    /// <summary>
    /// The task most recently started by <see cref="ScheduleSave"/> -- immediate or still
    /// debouncing -- so a test can await the outcome of a specific call deterministically instead
    /// of guessing at a wall-clock delay.
    /// </summary>
    internal Task PendingSave
    {
        get
        {
            lock (_gate)
            {
                return _pendingSave;
            }
        }
    }

    /// <summary>
    /// Schedules <paramref name="draft"/> to be saved: immediately, the first time this scheduler
    /// is ever asked to save, or after <see cref="_debounceInterval"/> of quiet every time after
    /// that. A no-op once <see cref="DisposeAsync"/> has run.
    /// </summary>
    /// <param name="draft">
    /// The draft version to persist once the debounce (if any) settles.
    /// </param>
    internal void ScheduleSave(FormVersion draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // Supersede whatever save is still pending -- its own write, if it has not started
            // yet, will never happen; RunAsync below treats that cancellation as ordinary, not an
            // error.
            _pendingCts?.Cancel();
            _pendingCts?.Dispose();

            var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
            _pendingCts = cts;

            var delay = _hasScheduledOnce ? _debounceInterval : TimeSpan.Zero;
            _hasScheduledOnce = true;
            _pendingSave = RunAsync(draft, delay, cts.Token);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Deliberate: this is the one place a host store's failure -- of any kind, since a host store can throw anything -- gets caught so it can be routed to _onFailure instead of faulting _pendingSave. A narrower catch would let an exception type this scheduler didn't anticipate still fault the pending task silently.")]
    private async Task RunAsync(FormVersion draft, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await _store.SaveDraftAsync(draft, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit, or the scheduler was disposed -- neither is a failure
            // this scheduler needs to surface; the newer edit's own save (if there is one) is
            // what actually matters.
        }
        catch (Exception ex)
        {
            // A genuine store failure -- a host I/O error, a transient outage, and so on.
            // Letting this fault _pendingSave would make it unobservable the moment a later
            // ScheduleSave overwrites the field (silent data loss), and would rethrow out of
            // DisposeAsync during teardown if this save happened to still be pending then.
            // Routing it to _onFailure instead keeps this scheduler itself fully usable for the
            // very next edit -- only the caller decides what, if anything, an author sees.
            _onFailure?.Invoke(ex);
        }
    }

    /// <summary>
    /// Cancels any pending or in-flight save and waits for it to actually stop before returning.
    /// Safe to call more than once.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Deliberate, and a last-resort guard rather than the primary handling path (RunAsync's own catch already routes a real failure to _onFailure and never lets this task fault): whatever reaches here must not rethrow out of component teardown.")]
    public async ValueTask DisposeAsync()
    {
        Task pending;
        CancellationTokenSource? pendingCts;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pending = _pendingSave;
            pendingCts = _pendingCts;
            _pendingCts = null;
        }

        if (pendingCts is not null)
        {
            await pendingCts.CancelAsync().ConfigureAwait(false);
            pendingCts.Dispose();
        }

        await _disposalCts.CancelAsync().ConfigureAwait(false);
        _disposalCts.Dispose();

        // RunAsync already turns its own cancellation into a no-op and routes any genuine save
        // failure to _onFailure rather than letting it fault this task, so nothing should ever
        // reach this catch in practice. It stays anyway as the last line of defence: awaiting
        // pending here gives disposal a deterministic point to wait past instead of leaving that
        // task to finish unobserved after this method has already returned, and a pending save
        // that faults for any reason must never rethrow out of component teardown.
        try
        {
            await pending.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }
}
