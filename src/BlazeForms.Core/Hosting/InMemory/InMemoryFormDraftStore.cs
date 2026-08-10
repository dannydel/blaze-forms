using System.Collections.Concurrent;

namespace BlazeForms.Hosting.InMemory;

/// <summary>
/// An <see cref="IFormDraftStore"/> that keeps in-progress fills in process. It ships for demos
/// and tests (PRD §9); it applies no retention or expiry policy, which is deliberately host
/// policy (OQ-2).
/// </summary>
public sealed class InMemoryFormDraftStore : IFormDraftStore
{
    private readonly ConcurrentDictionary<FormDraftKey, FormDraft> _drafts = new();

    /// <inheritdoc/>
    public Task<FormDraft?> LoadAsync(FormDraftKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_drafts.GetValueOrDefault(key));
    }

    /// <inheritdoc/>
    public Task SaveAsync(FormDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        _drafts[draft.Key] = draft;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(FormDraftKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        _drafts.TryRemove(key, out _);

        return Task.CompletedTask;
    }
}
