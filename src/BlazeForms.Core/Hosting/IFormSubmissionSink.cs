namespace BlazeForms.Hosting;

/// <summary>
/// Where a completed fill goes. This is the library's boundary: BlazeForms hands over the
/// envelope and the host owns everything after — workflow, notification, payment, storage
/// (PRD §2, §9).
/// </summary>
public interface IFormSubmissionSink
{
    /// <summary>
    /// Accepts a completed submission.
    /// </summary>
    /// <param name="envelope">
    /// The submission, with answers keyed by node ID and hidden fields absent.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the operation.
    /// </param>
    /// <returns>
    /// A task that completes once the host has taken responsibility for the submission.
    /// </returns>
    Task SubmitAsync(FormSubmissionEnvelope envelope, CancellationToken cancellationToken = default);
}
