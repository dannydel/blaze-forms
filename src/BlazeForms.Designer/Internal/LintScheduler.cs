using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Linting;

namespace BlazeForms.Designer.Internal;

/// <summary>
/// Debounced re-linting behind <see cref="Linting.LinterDock"/> (PRD §8), the same shape
/// <see cref="AutosaveScheduler"/> gives the autosave: the very first lint a scheduler is ever
/// asked to run fires immediately, so the dock shows the initial draft's findings without
/// waiting; every run after that waits for <see cref="_debounceInterval"/> of quiet before it
/// actually lints, and a run still pending when a newer one is scheduled is superseded -- its own
/// lint pass never happens -- so a burst of rapid mutations (e.g. holding <c>Alt+↓</c>) always
/// coalesces into one pass over the last of them.
/// </summary>
internal sealed class LintScheduler : IAsyncDisposable
{
    private readonly TimeSpan _debounceInterval;
    private readonly Action<IReadOnlyList<LintResult>> _onLinted;
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly Lock _gate = new();
    private CancellationTokenSource? _pendingCts;
    private Task _pendingLint = Task.CompletedTask;
    private bool _hasScheduledOnce;
    private bool _disposed;

    /// <summary>
    /// Creates a scheduler that lints on demand.
    /// </summary>
    /// <param name="onLinted">
    /// Invoked with the result of every lint pass that actually runs -- never for one a later
    /// <see cref="ScheduleLint"/> call superseded before it started. Called off the caller's own
    /// synchronization context (the debounce delay resumes on the thread pool), so a UI consumer
    /// must dispatch back to its own renderer itself, the same way
    /// <see cref="DesignerEditContext.AutosaveFailed"/>'s subscribers already do for
    /// <see cref="AutosaveScheduler"/>.
    /// </param>
    /// <param name="debounceInterval">
    /// How long a lint after the first waits for mutations to stop arriving before it actually
    /// runs. <see langword="null"/> selects a 300 ms default.
    /// </param>
    internal LintScheduler(Action<IReadOnlyList<LintResult>> onLinted, TimeSpan? debounceInterval = null)
    {
        ArgumentNullException.ThrowIfNull(onLinted);

        _onLinted = onLinted;
        _debounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(300);
    }

    /// <summary>
    /// The task most recently started by <see cref="ScheduleLint"/> -- immediate or still
    /// debouncing -- so a test can await the outcome of a specific call deterministically instead
    /// of guessing at a wall-clock delay.
    /// </summary>
    internal Task PendingLint
    {
        get
        {
            lock (_gate)
            {
                return _pendingLint;
            }
        }
    }

    /// <summary>
    /// Schedules <paramref name="definition"/> to be linted: immediately, the first time this
    /// scheduler is ever asked to lint, or after <see cref="_debounceInterval"/> of quiet every
    /// time after that. A no-op once <see cref="DisposeAsync"/> has run.
    /// </summary>
    /// <param name="definition">
    /// The definition to lint once the debounce (if any) settles.
    /// </param>
    internal void ScheduleLint(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // Supersede whatever lint is still pending -- its own pass, if it has not started
            // yet, will never happen; RunAsync below treats that cancellation as ordinary.
            _pendingCts?.Cancel();
            _pendingCts?.Dispose();

            var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token);
            _pendingCts = cts;

            var delay = _hasScheduledOnce ? _debounceInterval : TimeSpan.Zero;
            _hasScheduledOnce = true;
            _pendingLint = RunAsync(definition, delay, cts.Token);
        }
    }

    private async Task RunAsync(FormDefinition definition, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var results = FormLinter.CreateDefault().Lint(definition);
            _onLinted(results);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer mutation, or the scheduler was disposed -- neither is a
            // failure this scheduler needs to surface; the newer mutation's own lint pass (if
            // there is one) is what actually matters.
        }
    }

    /// <summary>
    /// Cancels any pending or in-flight lint and waits for it to actually stop before returning.
    /// Safe to call more than once.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Deliberate, and a last-resort guard rather than the primary handling path (RunAsync's own catch already turns its own cancellation into a no-op and never lets this task fault): whatever reaches here must not rethrow out of component teardown, the same rationale AutosaveScheduler.DisposeAsync documents for itself.")]
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
            pending = _pendingLint;
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

        // RunAsync already turns its own cancellation into a no-op, so nothing should ever reach
        // here in practice -- this stays as the last line of defence: awaiting pending gives
        // disposal a deterministic point to wait past instead of leaving that task to finish
        // unobserved after this method has already returned.
        try
        {
            await pending.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }
}
