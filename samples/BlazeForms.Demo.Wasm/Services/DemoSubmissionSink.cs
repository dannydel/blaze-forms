using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using BlazeForms.Hosting;

namespace BlazeForms.Demo.Wasm.Services;

/// <summary>
/// The demo host's <see cref="IFormSubmissionSink"/>: keeps every completed submission in the
/// current tab's memory, keyed by <see cref="FormSubmissionEnvelope.SubmissionId"/>, so the
/// demo's <c>Submission</c> page can look one back up after <c>FormRenderer</c> hands it off (PRD
/// §9). A minimal equivalent of the sample host's <c>SampleSubmissionSink</c> — duplicated rather
/// than referenced because the demo never depends on the sample project.
/// </summary>
/// <remarks>
/// Registered as a WASM singleton in <c>Program.cs</c>, which is already per-browser-tab — a
/// refresh discards it, matching the demo's "everything is in-browser memory" banner.
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by the DI container as a singleton (Program.cs) and injected into Fill.razor/Submission.razor/Home.razor — the analyzer cannot see either path.")]
internal sealed class DemoSubmissionSink : IFormSubmissionSink
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
    /// this tab.
    /// </returns>
    public FormSubmissionEnvelope? TryGet(string submissionId) =>
        _submissions.TryGetValue(submissionId, out var envelope) ? envelope : null;

    /// <summary>
    /// Every submission captured this tab, newest first, for the demo home page's list of links.
    /// </summary>
    public IReadOnlyList<FormSubmissionEnvelope> All =>
        [.. _submissions.Values.OrderByDescending(envelope => envelope.SubmittedAt)];
}
