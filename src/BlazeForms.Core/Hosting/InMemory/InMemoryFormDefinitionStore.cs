using BlazeForms.Versioning;

namespace BlazeForms.Hosting.InMemory;

/// <summary>
/// An <see cref="IFormDefinitionStore"/> that keeps everything in process. It ships for demos
/// and tests (PRD §9) and doubles as the reference behaviour a host implementation should match:
/// version numbers start at 1 and only ever increase, publishing consumes the working draft, and
/// a published version is never rewritten.
/// </summary>
public sealed class InMemoryFormDefinitionStore : IFormDefinitionStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, FormHistory> _forms = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates an empty store.
    /// </summary>
    /// <param name="timeProvider">
    /// The clock used to stamp publish and retire times, or <see langword="null"/> for
    /// <see cref="TimeProvider.System"/>.
    /// </param>
    public InMemoryFormDefinitionStore(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc/>
    public Task<FormVersion?> GetVersionAsync(
        string formId,
        int version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var found = _forms.TryGetValue(formId, out var history)
                ? history.Versions.GetValueOrDefault(version)
                : null;

            return Task.FromResult(found);
        }
    }

    /// <inheritdoc/>
    public Task<FormVersion?> GetLatestPublishedVersionAsync(
        string formId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_forms.TryGetValue(formId, out var history))
            {
                return Task.FromResult<FormVersion?>(null);
            }

            // Only the newest version is a candidate. Falling back to an earlier published version
            // once the newest is retired would be rollback-in-place, which PRD §7 forbids.
            var newest = history.Versions
                .OrderByDescending(entry => entry.Key)
                .Select(entry => entry.Value)
                .FirstOrDefault();

            return Task.FromResult(newest?.State == FormLifecycleState.Published ? newest : null);
        }
    }

    /// <inheritdoc/>
    public Task<FormVersion?> GetDraftAsync(string formId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var draft = _forms.TryGetValue(formId, out var history) ? history.Draft : null;

            return Task.FromResult(draft);
        }
    }

    /// <inheritdoc/>
    public Task SaveDraftAsync(FormVersion draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        if (draft.State != FormLifecycleState.Draft)
        {
            throw new ArgumentException(
                $"Only a draft can be saved; version {draft.Version} of form '{draft.FormId}' is {draft.State}. Published versions are immutable.",
                nameof(draft));
        }

        if (string.IsNullOrWhiteSpace(draft.FormId))
        {
            throw new ArgumentException("A draft must carry a form ID.", nameof(draft));
        }

        if (!string.Equals(draft.FormId, draft.Definition.Id, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The draft's form ID '{draft.FormId}' does not match its definition's ID '{draft.Definition.Id}'. A version and the definition it holds must describe the same form.",
                nameof(draft));
        }

        lock (_gate)
        {
            GetOrAdd(draft.FormId).Draft = draft with { Version = FormLifecycle.UnpublishedVersion };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteDraftAsync(string formId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_forms.TryGetValue(formId, out var history))
            {
                history.Draft = null;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<FormVersion> PublishAsync(
        string formId,
        string changeNote,
        string author,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);
        ArgumentException.ThrowIfNullOrWhiteSpace(changeNote);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_forms.TryGetValue(formId, out var history) || history.Draft is null)
            {
                throw new InvalidOperationException(
                    $"Form '{formId}' has no draft to publish.");
            }

            var nextVersion = history.Versions.Count == 0 ? 1 : history.Versions.Keys.Max() + 1;

            var published = FormLifecycle.Publish(
                history.Draft,
                nextVersion,
                changeNote,
                author,
                _timeProvider.GetUtcNow());

            history.Versions[nextVersion] = published;
            history.Draft = null;

            return Task.FromResult(published);
        }
    }

    /// <inheritdoc/>
    public Task RetireAsync(string formId, int version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_forms.TryGetValue(formId, out var history)
                || !history.Versions.TryGetValue(version, out var published))
            {
                throw new InvalidOperationException(
                    $"Form '{formId}' has no version {version} to retire.");
            }

            history.Versions[version] = FormLifecycle.Retire(published, _timeProvider.GetUtcNow());
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FormVersionSummary>> ListVersionsAsync(
        string formId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_forms.TryGetValue(formId, out var history))
            {
                return Task.FromResult<IReadOnlyList<FormVersionSummary>>([]);
            }

            IReadOnlyList<FormVersionSummary> summaries =
            [
                .. history.Versions
                    .OrderBy(entry => entry.Key)
                    .Select(entry => FormLifecycle.Summarize(entry.Value)),
            ];

            return Task.FromResult(summaries);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FormVersionSummary>> ListFormsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            IReadOnlyList<FormVersionSummary> summaries =
            [
                .. _forms.Values
                    .Select(Newest)
                    .OfType<FormVersion>()
                    .Select(version => FormLifecycle.Summarize(version)),
            ];

            return Task.FromResult(summaries);
        }
    }

    private static FormVersion? Newest(FormHistory history) =>
        history.Draft
        ?? history.Versions
            .OrderByDescending(entry => entry.Key)
            .Select(entry => entry.Value)
            .FirstOrDefault();

    private FormHistory GetOrAdd(string formId)
    {
        if (!_forms.TryGetValue(formId, out var history))
        {
            history = new FormHistory();
            _forms[formId] = history;
        }

        return history;
    }

    private sealed class FormHistory
    {
        public FormVersion? Draft { get; set; }

        public Dictionary<int, FormVersion> Versions { get; } = [];
    }
}
