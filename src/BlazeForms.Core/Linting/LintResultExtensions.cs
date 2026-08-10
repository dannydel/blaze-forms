namespace BlazeForms.Linting;

/// <summary>
/// Convenience queries over a set of lint results. <see cref="HasBlockingIssues"/> is the publish
/// gate (PRD §7): the host asks whether any blocking result stands before it lets a version
/// publish, and the lifecycle itself stays untouched.
/// </summary>
public static class LintResultExtensions
{
    /// <summary>
    /// Whether any result blocks publishing.
    /// </summary>
    /// <param name="results">
    /// The results to inspect.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when at least one result has severity
    /// <see cref="LintSeverity.Blocking"/>.
    /// </returns>
    public static bool HasBlockingIssues(this IEnumerable<LintResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return results.Any(result => result.Severity == LintSeverity.Blocking);
    }

    /// <summary>
    /// Filters a set of results to the blocking ones, in their original order.
    /// </summary>
    /// <param name="results">
    /// The results to filter.
    /// </param>
    /// <returns>
    /// The results whose severity is <see cref="LintSeverity.Blocking"/>.
    /// </returns>
    public static IEnumerable<LintResult> Blocking(this IEnumerable<LintResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return results.Where(result => result.Severity == LintSeverity.Blocking);
    }
}
