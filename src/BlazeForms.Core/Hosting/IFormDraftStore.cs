namespace BlazeForms.Hosting;

/// <summary>
/// Where in-progress fills live, keyed by form, definition version, and respondent (PRD §9).
/// Drafts are host-side (D6); retention and expiry are host policy (OQ-2).
/// </summary>
public interface IFormDraftStore
{
    /// <summary>
    /// Loads a respondent's in-progress fill.
    /// </summary>
    /// <param name="key">
    /// What to load.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the operation.
    /// </param>
    /// <returns>
    /// The draft, or <see langword="null"/> when there is nothing to resume.
    /// </returns>
    Task<FormDraft?> LoadAsync(FormDraftKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a respondent's in-progress fill, replacing any draft already held for the same key.
    /// </summary>
    /// <param name="draft">
    /// The draft to save.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the operation.
    /// </param>
    /// <returns>
    /// A task that completes once the draft is stored.
    /// </returns>
    Task SaveAsync(FormDraft draft, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards a respondent's in-progress fill, typically once it has been submitted.
    /// </summary>
    /// <param name="key">
    /// What to discard.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the operation.
    /// </param>
    /// <returns>
    /// A task that completes once the draft is gone. Deleting an absent draft is not an error.
    /// </returns>
    Task DeleteAsync(FormDraftKey key, CancellationToken cancellationToken = default);
}
