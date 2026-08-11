using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Versioning;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// A test double whose <see cref="SaveDraftAsync"/> throws a non-cancellation exception for its
/// first <c>failuresBeforeSucceeding</c> calls, then delegates to a real
/// <see cref="InMemoryFormDefinitionStore"/> for every call after that -- exercises
/// <see cref="Internal.AutosaveScheduler"/>'s and <see cref="DesignerEditContext"/>'s handling of
/// a genuine store failure (host I/O error, transient outage) as distinct from an ordinary
/// superseded-by-a-newer-edit cancellation.
/// </summary>
internal sealed class ThrowingFormDefinitionStore : IFormDefinitionStore
{
    private readonly InMemoryFormDefinitionStore _inner = new();
    private readonly int _failuresBeforeSucceeding;
    private int _saveAttempts;

    internal ThrowingFormDefinitionStore(int failuresBeforeSucceeding) =>
        _failuresBeforeSucceeding = failuresBeforeSucceeding;

    /// <summary>
    /// How many calls to <see cref="SaveDraftAsync"/> actually reached and completed against the
    /// inner store, rather than throwing.
    /// </summary>
    internal int SuccessfulSaveCount { get; private set; }

    public Task<FormVersion?> GetVersionAsync(string formId, int version, CancellationToken cancellationToken = default) =>
        _inner.GetVersionAsync(formId, version, cancellationToken);

    public Task<FormVersion?> GetLatestPublishedVersionAsync(string formId, CancellationToken cancellationToken = default) =>
        _inner.GetLatestPublishedVersionAsync(formId, cancellationToken);

    public Task<FormVersion?> GetDraftAsync(string formId, CancellationToken cancellationToken = default) =>
        _inner.GetDraftAsync(formId, cancellationToken);

    public async Task SaveDraftAsync(FormVersion draft, CancellationToken cancellationToken = default)
    {
        _saveAttempts++;

        if (_saveAttempts <= _failuresBeforeSucceeding)
        {
            throw new InvalidOperationException("Simulated store failure -- e.g. a transient host outage.");
        }

        await _inner.SaveDraftAsync(draft, cancellationToken).ConfigureAwait(false);
        SuccessfulSaveCount++;
    }

    public Task DeleteDraftAsync(string formId, CancellationToken cancellationToken = default) =>
        _inner.DeleteDraftAsync(formId, cancellationToken);

    public Task<FormVersion> PublishAsync(
        string formId,
        string changeNote,
        string author,
        CancellationToken cancellationToken = default) =>
        _inner.PublishAsync(formId, changeNote, author, cancellationToken);

    public Task RetireAsync(string formId, int version, CancellationToken cancellationToken = default) =>
        _inner.RetireAsync(formId, version, cancellationToken);

    public Task<IReadOnlyList<FormVersionSummary>> ListVersionsAsync(
        string formId,
        CancellationToken cancellationToken = default) =>
        _inner.ListVersionsAsync(formId, cancellationToken);

    public Task<IReadOnlyList<FormVersionSummary>> ListFormsAsync(CancellationToken cancellationToken = default) =>
        _inner.ListFormsAsync(cancellationToken);
}
