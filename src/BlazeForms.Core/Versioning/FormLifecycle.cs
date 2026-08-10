using BlazeForms.Definitions;

namespace BlazeForms.Versioning;

/// <summary>
/// The state transitions of PRD §7, as pure functions over <see cref="FormVersion"/>. Every
/// transition returns a new value and leaves its input untouched, which is what makes "published
/// means immutable" mechanical rather than a matter of discipline.
/// </summary>
/// <remarks>
/// Publish gating on lint results is deliberately not here: this slice models the lifecycle so
/// the store contract is implementable, and the linter arrives in its own slice (PRD §8).
/// </remarks>
public static class FormLifecycle
{
    /// <summary>
    /// The version number a draft carries before it has ever been published.
    /// </summary>
    public const int UnpublishedVersion = 0;

    /// <summary>
    /// Starts a new draft over the supplied content.
    /// </summary>
    /// <param name="definition">
    /// The definition content the draft holds.
    /// </param>
    /// <returns>
    /// An unpublished draft version.
    /// </returns>
    public static FormVersion CreateDraft(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new FormVersion
        {
            FormId = definition.Id,
            Version = UnpublishedVersion,
            State = FormLifecycleState.Draft,
            Definition = definition,
        };
    }

    /// <summary>
    /// Publishes a draft as a numbered, immutable version.
    /// </summary>
    /// <param name="draft">
    /// The draft to publish.
    /// </param>
    /// <param name="version">
    /// The version number to assign, from 1 upward.
    /// </param>
    /// <param name="changeNote">
    /// The author's note, required by PRD §7 and kept in version history.
    /// </param>
    /// <param name="author">
    /// An opaque host-supplied key for the publishing author.
    /// </param>
    /// <param name="publishedAt">
    /// The publish timestamp.
    /// </param>
    /// <returns>
    /// A new published version; <paramref name="draft"/> is unchanged.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="draft"/> is not in the <see cref="FormLifecycleState.Draft"/> state.
    /// Published versions are never republished or edited.
    /// </exception>
    public static FormVersion Publish(
        FormVersion draft,
        int version,
        string changeNote,
        string author,
        DateTimeOffset publishedAt)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(changeNote);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        if (draft.State != FormLifecycleState.Draft)
        {
            throw new InvalidOperationException(
                $"Form '{draft.FormId}' version {draft.Version} is {draft.State} and cannot be published. Published versions are immutable; publish a new draft instead.");
        }

        return draft with
        {
            Version = version,
            State = FormLifecycleState.Published,
            ChangeNote = changeNote,
            Author = author,
            PublishedAt = publishedAt,
            RetiredAt = null,
        };
    }

    /// <summary>
    /// Retires a published version so it stops accepting new fills. Existing submissions stay
    /// renderable.
    /// </summary>
    /// <param name="published">
    /// The published version to retire.
    /// </param>
    /// <param name="retiredAt">
    /// The retirement timestamp.
    /// </param>
    /// <returns>
    /// A new retired version; <paramref name="published"/> is unchanged.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="published"/> is not in the <see cref="FormLifecycleState.Published"/>
    /// state.
    /// </exception>
    public static FormVersion Retire(FormVersion published, DateTimeOffset retiredAt)
    {
        ArgumentNullException.ThrowIfNull(published);

        if (published.State != FormLifecycleState.Published)
        {
            throw new InvalidOperationException(
                $"Form '{published.FormId}' version {published.Version} is {published.State}, so there is nothing to retire.");
        }

        return published with
        {
            State = FormLifecycleState.Retired,
            RetiredAt = retiredAt,
        };
    }

    /// <summary>
    /// Starts a new draft from an existing version's content. This is how an older version is
    /// "restored": its content becomes the next published version, and nothing about the
    /// original changes (PRD §7).
    /// </summary>
    /// <param name="version">
    /// The version whose content the new draft starts from.
    /// </param>
    /// <returns>
    /// An unpublished draft holding the same definition.
    /// </returns>
    public static FormVersion ReviseAsDraft(FormVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return version with
        {
            Version = UnpublishedVersion,
            State = FormLifecycleState.Draft,
            ChangeNote = null,
            Author = null,
            PublishedAt = null,
            RetiredAt = null,
        };
    }

    /// <summary>
    /// Projects a version onto the summary a version-history or library listing shows.
    /// </summary>
    /// <param name="version">
    /// The version to summarize.
    /// </param>
    /// <param name="submissionCount">
    /// How many submissions were captured against this version.
    /// </param>
    /// <returns>
    /// A summary carrying no definition content.
    /// </returns>
    public static FormVersionSummary Summarize(FormVersion version, int submissionCount = 0)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentOutOfRangeException.ThrowIfNegative(submissionCount);

        return new FormVersionSummary
        {
            FormId = version.FormId,
            Name = version.Definition.Name,
            Version = version.Version,
            State = version.State,
            ChangeNote = version.ChangeNote,
            Author = version.Author,
            PublishedAt = version.PublishedAt,
            RetiredAt = version.RetiredAt,
            SubmissionCount = submissionCount,
        };
    }
}
