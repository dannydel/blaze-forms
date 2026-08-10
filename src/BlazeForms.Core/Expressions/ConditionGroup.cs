namespace BlazeForms.Expressions;

/// <summary>
/// A serializable expression tree: <c>{ join, conditions[] }</c> (PRD §6). One model serves
/// visibility now and grows to cover calc and cross-field validation later.
/// </summary>
public sealed record ConditionGroup
{
    private readonly IReadOnlyList<Condition>? _conditions;

    /// <summary>
    /// How the conditions combine. Defaults to <see cref="ConditionJoin.All"/>.
    /// </summary>
    public ConditionJoin Join { get; init; }

    /// <summary>
    /// The clauses of the expression, in the order the rule editor shows them. Reads as empty
    /// when a document omits it.
    /// </summary>
    public IReadOnlyList<Condition> Conditions
    {
        get => _conditions ?? [];
        init => _conditions = value is null ? null : Array.AsReadOnly<Condition>([.. value]);
    }
}
