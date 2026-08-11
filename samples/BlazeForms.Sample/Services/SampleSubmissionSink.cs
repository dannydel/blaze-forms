using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using BlazeForms.Hosting;

namespace BlazeForms.Sample.Services;

/// <summary>
/// The sample host's <see cref="IFormSubmissionSink"/>: keeps every completed submission in
/// process, keyed by <see cref="FormSubmissionEnvelope.SubmissionId"/>, so the sample's
/// <c>Submission</c> page can look one back up after <c>FormRenderer</c> hands it off (PRD §9).
/// A real host would replace this with its own workflow, notification, or storage integration —
/// BlazeForms owns nothing past <see cref="SubmitAsync"/>.
/// </summary>
/// <remarks>
/// Registered as a singleton in <c>Program.cs</c> so a submission survives across circuits and
/// requests for the lifetime of the process, matching <see cref="Hosting.InMemory.InMemoryFormDefinitionStore"/>.
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by the DI container as a singleton (Program.cs) and injected into Fill.razor/Submission.razor/Home.razor — the analyzer cannot see either path.")]
internal sealed class SampleSubmissionSink : IFormSubmissionSink
{
    private readonly ConcurrentDictionary<string, FormSubmissionEnvelope> _submissions = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task SubmitAsync(FormSubmissionEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();

        _submissions[envelope.SubmissionId] = envelope;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Looks up a previously captured submission by its id.
    /// </summary>
    /// <param name="submissionId">
    /// The <see cref="FormSubmissionEnvelope.SubmissionId"/> to look up.
    /// </param>
    /// <returns>
    /// The envelope, or <see langword="null"/> when no submission with that id has been captured
    /// this process.
    /// </returns>
    public FormSubmissionEnvelope? TryGet(string submissionId) =>
        _submissions.TryGetValue(submissionId, out var envelope) ? envelope : null;

    /// <summary>
    /// Every submission captured this process, newest first, for the sample home page's list of
    /// links.
    /// </summary>
    public IReadOnlyList<FormSubmissionEnvelope> All =>
        [.. _submissions.Values.OrderByDescending(envelope => envelope.SubmittedAt)];
}
