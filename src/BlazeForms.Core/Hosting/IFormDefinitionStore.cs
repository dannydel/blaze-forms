using BlazeForms.Versioning;

namespace BlazeForms.Hosting;

/// <summary>
/// Where definitions live. BlazeForms ships no storage: hosts implement this and register it
/// with DI (PRD §9, D6). An in-memory implementation ships for demos and tests.
/// </summary>
/// <remarks>
/// Implementations must honour PRD §7: published versions are never rewritten, version numbers
/// increase monotonically from 1, and a form has at most one draft at a time.
/// </remarks>
public interface IFormDefinitionStore
{
    /// <summary>
    /// Loads one version of a form.
    /// </summary>
    /// <param name="formId">
    /// The form to load from.
    /// </param>
    /// <param name="version">
    /// The version number to load.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the operation.
    /// </param>
    /// <returns>
    /// The version, or <see langword="null"/> when the form or version is unknown.
    /// </returns>
    Task<FormVersion?> GetVersionAsync(string formId, int version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the highest-numbered version that still accepts fills.
    /// </summary>
    /// <param name="formId">
    /// The form to load from.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the operation.
    /// </param>
    /// <returns>
    /// The form's newest version when it is published, and otherwise <see langword="null"/>.
    /// Retirement supersedes every lower-numbered version: retiring the highest-numbered version
    /// leaves the form with nothing to fill rather than reopening its predecessor, because falling
    /// back would be the rollback-in-place PRD §7 forbids.
    /// </returns>
    Task<FormVersion?> GetLatestPublishedVersionAsync(string formId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a form's working draft.
    /// </summary>
    /// <param name="formId">
    /// The form to load from.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the operation.
    /// </param>
    /// <returns>
    /// The draft, or <see langword="null"/> when the form has no unpublished edits.
    /// </returns>
    Task<FormVersion?> GetDraftAsync(string formId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a form's working draft, replacing any draft already held.
    /// </summary>
    /// <param name="draft">
    /// The draft to save. Must be in the <see cref="FormLifecycleState.Draft"/> state.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the operation.
    /// </param>
    /// <returns>
    /// A task that completes once the draft is stored.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="draft"/> is not in the <see cref="FormLifecycleState.Draft"/> state, or its
    /// <see cref="FormVersion.FormId"/> does not match the identifier of the definition it holds.
    /// </exception>
    Task SaveDraftAsync(FormVersion draft, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards a form's working draft, throwing away its unpublished edits. Published versions are
    /// untouched — PRD §7's "only never-published drafts can be deleted" is about versions, and a
    /// working draft has by definition never been published.
    /// </summary>
    /// <param name="formId">
    /// The form whose draft is discarded.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the operation.
    /// </param>
    /// <returns>
    /// A task that completes once the draft is gone. Deleting an absent draft is not an error.
    /// </returns>
    Task DeleteDraftAsync(string formId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a form's working draft as the next immutable version.
    /// </summary>
    /// <param name="formId">
    /// The form to publish.
    /// </param>
    /// <param name="changeNote">
    /// The author's note, required by PRD §7 and kept in version history.
    /// </param>
    /// <param name="author">
    /// An opaque host-supplied key for the publishing author.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the operation.
    /// </param>
    /// <returns>
    /// The version that was created, numbered one above the form's highest existing version.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The form has no working draft to publish.
    /// </exception>
    Task<FormVersion> PublishAsync(
        string formId,
        string changeNote,
        string author,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a published version so it stops accepting new fills. Existing submissions stay
    /// renderable.
    /// </summary>
    /// <param name="formId">
    /// The form to retire a version of.
    /// </param>
    /// <param name="version">
    /// The published version number to retire.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the operation.
    /// </param>
    /// <returns>
    /// A task that completes once the version is retired.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The form has no such version, or that version is not in the
    /// <see cref="FormLifecycleState.Published"/> state. There is no unpublish and no
    /// un-retire (PRD §7).
    /// </exception>
    Task RetireAsync(string formId, int version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a form's version history, oldest first.
    /// </summary>
    /// <param name="formId">
    /// The form to list versions of.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the operation.
    /// </param>
    /// <returns>
    /// One summary per published or retired version. Working drafts are reached through
    /// <see cref="GetDraftAsync"/> instead.
    /// </returns>
    Task<IReadOnlyList<FormVersionSummary>> ListVersionsAsync(
        string formId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the forms this store holds, for the library surface (PRD §4.4).
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the operation.
    /// </param>
    /// <returns>
    /// One summary per form, describing its newest version — the draft when one exists, and
    /// otherwise the highest-numbered published or retired version.
    /// </returns>
    Task<IReadOnlyList<FormVersionSummary>> ListFormsAsync(CancellationToken cancellationToken = default);
}
