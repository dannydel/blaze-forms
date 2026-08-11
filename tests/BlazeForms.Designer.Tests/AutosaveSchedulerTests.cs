using BlazeForms.Designer.Internal;
using BlazeForms.Versioning;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="AutosaveScheduler"/> in isolation, with a short debounce interval so these
/// run fast without sacrificing determinism: the first save it is ever asked to make fires
/// immediately (PRD §7's "persisted on the first edit"), every save after that debounces and
/// coalesces a burst of rapid calls into one write, and disposal cancels a still-pending save and
/// is safe to call more than once.
/// </summary>
public sealed class AutosaveSchedulerTests
{
    private static readonly TimeSpan ShortDebounce = TimeSpan.FromMilliseconds(30);

    [Fact]
    public async Task TheFirstScheduledSaveWritesImmediately()
    {
        var store = new SaveTrackingFormDefinitionStore();
        var scheduler = new AutosaveScheduler(store, ShortDebounce);
        var draft = FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition("form-1"));

        scheduler.ScheduleSave(draft);
        await scheduler.PendingSave.ConfigureAwait(true);

        Assert.Equal(1, store.SaveCount);
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task ASecondScheduledSaveDebouncesRatherThanWritingImmediately()
    {
        var store = new SaveTrackingFormDefinitionStore();
        var scheduler = new AutosaveScheduler(store, ShortDebounce);
        var draft = FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition("form-1"));

        scheduler.ScheduleSave(draft);
        await scheduler.PendingSave.ConfigureAwait(true);
        Assert.Equal(1, store.SaveCount);

        scheduler.ScheduleSave(draft);

        // The debounce has not elapsed yet -- the second save must not have written already.
        Assert.Equal(1, store.SaveCount);

        await scheduler.PendingSave.ConfigureAwait(true);
        Assert.Equal(2, store.SaveCount);
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task RapidSuccessiveSavesAfterTheFirstCoalesceIntoOneWrite()
    {
        var store = new SaveTrackingFormDefinitionStore();
        var scheduler = new AutosaveScheduler(store, ShortDebounce);
        var draft = FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition("form-1"));

        scheduler.ScheduleSave(draft);
        await scheduler.PendingSave.ConfigureAwait(true);
        Assert.Equal(1, store.SaveCount);

        // Three more schedules land well inside the debounce window of one another -- each
        // supersedes the last, so only the final one should ever actually write.
        scheduler.ScheduleSave(draft);
        scheduler.ScheduleSave(draft);
        scheduler.ScheduleSave(draft);

        await scheduler.PendingSave.ConfigureAwait(true);
        Assert.Equal(2, store.SaveCount);
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task DisposeCancelsAPendingSaveBeforeItWrites()
    {
        var store = new SaveTrackingFormDefinitionStore();
        var scheduler = new AutosaveScheduler(store, ShortDebounce);
        var draft = FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition("form-1"));

        scheduler.ScheduleSave(draft); // immediate -- lets the "first save" behaviour out of the way
        await scheduler.PendingSave.ConfigureAwait(true);
        Assert.Equal(1, store.SaveCount);

        scheduler.ScheduleSave(draft); // debounced -- still pending when disposal runs below
        await scheduler.DisposeAsync();

        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task DisposeIsIdempotent()
    {
        var store = new SaveTrackingFormDefinitionStore();
        var scheduler = new AutosaveScheduler(store, ShortDebounce);

        await scheduler.DisposeAsync();
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task ANonCancellationSaveFailureIsRoutedToOnFailureAndLeavesThePendingTaskUnfaulted()
    {
        var store = new ThrowingFormDefinitionStore(failuresBeforeSucceeding: 1);
        Exception? observed = null;
        var scheduler = new AutosaveScheduler(store, ShortDebounce, onFailure: ex => observed = ex);
        var draft = FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition("form-1"));

        scheduler.ScheduleSave(draft); // immediate -- this is the one call that throws
        var pending = scheduler.PendingSave;
        await pending.ConfigureAwait(true);

        // The failure reached onFailure rather than faulting the task a later ScheduleSave would
        // just silently overwrite.
        Assert.Equal(TaskStatus.RanToCompletion, pending.Status);
        Assert.IsType<InvalidOperationException>(observed);

        // The scheduler itself stays usable: the very next save succeeds normally.
        scheduler.ScheduleSave(draft);
        await scheduler.PendingSave.ConfigureAwait(true);
        Assert.Equal(1, store.SuccessfulSaveCount);

        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsyncDoesNotRethrowWhenTheJustCompletedSaveHadFailed()
    {
        var store = new ThrowingFormDefinitionStore(failuresBeforeSucceeding: int.MaxValue);
        var scheduler = new AutosaveScheduler(store, ShortDebounce, onFailure: _ => { });
        var draft = FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition("form-1"));

        scheduler.ScheduleSave(draft); // immediate -- always throws for this store
        await scheduler.PendingSave.ConfigureAwait(true);

        await scheduler.DisposeAsync(); // must not rethrow the failure that already happened
    }

    [Fact]
    public async Task ScheduleSaveAfterDisposeIsANoOp()
    {
        var store = new SaveTrackingFormDefinitionStore();
        var scheduler = new AutosaveScheduler(store, ShortDebounce);
        await scheduler.DisposeAsync();

        scheduler.ScheduleSave(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition("form-1")));

        Assert.Equal(0, store.SaveCount);
    }
}
