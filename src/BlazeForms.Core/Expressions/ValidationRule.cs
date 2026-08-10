namespace BlazeForms.Expressions;

/// <summary>
/// A cross-field validation rule: <c>{ target, message, expression }</c> over the same
/// expression tree visibility uses (PRD §6).
/// </summary>
public sealed record ValidationRule
{
    /// <summary>
    /// The identifier of the node the failure is reported against, so the error summary can
    /// anchor a link to it.
    /// </summary>
    public required string Target { get; init; }

    /// <summary>
    /// The message shown when the rule fires. Plain text always, and it must state the remedy
    /// rather than the failure — advisory lint A11Y-06 otherwise (PRD §8).
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// The condition that describes the *invalid* state: when it evaluates to
    /// <see langword="true"/>, <see cref="Message"/> is reported against <see cref="Target"/>.
    /// </summary>
    public required ConditionGroup Expression { get; init; }
}
