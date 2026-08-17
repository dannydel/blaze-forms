namespace BlazeForms.Internal;

/// <summary>
/// Builds the composite key <see cref="FormRenderer"/> and <see cref="CrossFieldValidator"/> use
/// to address one child field within one row of a repeating group — the per-(child, row) error
/// and validated-state entries a plain node id cannot express, since the same child node id
/// repeats once per row (repeating-groups-plan.md, D-3).
/// </summary>
internal static class RepeatingFieldKeys
{
    /// <summary>
    /// The separator between a child node id and a row id in a composite key. Deliberately
    /// outside <c>FormIds</c>' own generated-id alphabet (<c>[a-z0-9-]</c>), so a composite key
    /// can never collide with a plain top-level node id no matter what either half contains.
    /// </summary>
    private const char Separator = '|';

    /// <summary>
    /// Builds the composite key for one child within one row.
    /// </summary>
    /// <param name="childId">
    /// The identifier of the child node inside the repeating group.
    /// </param>
    /// <param name="rowId">
    /// The identifier of the row the child's answer belongs to.
    /// </param>
    /// <returns>
    /// A key of the form <c>"{childId}|{rowId}"</c>, safe to use anywhere a plain node id would
    /// be used as a dictionary key.
    /// </returns>
    internal static string ChildKey(string childId, string rowId) => $"{childId}{Separator}{rowId}";
}
