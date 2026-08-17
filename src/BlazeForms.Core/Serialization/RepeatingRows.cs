using BlazeForms.Definitions;

namespace BlazeForms.Serialization;

/// <summary>
/// The answer a respondent has entered into a <see cref="NodeType.Repeating"/> group (PRD §5):
/// an ordered list of rows, each keyed by a stable, opaque row identifier
/// (<see cref="FormIds.NewRowId"/>) that survives add, remove, reorder, draft resume, and the
/// submission envelope. Immutable — every mutator returns a new instance rather than changing
/// this one (AGENTS.md invariant #5's "answers are values", extended to a repeating group's own
/// answer).
/// </summary>
public sealed record RepeatingRows
{
    private readonly IReadOnlyList<RepeatingRow>? _rows;

    /// <summary>
    /// A <see cref="RepeatingRows"/> with no rows — the seed value for a group whose
    /// <see cref="FormNode.MinRows"/> is <see langword="null"/> or zero.
    /// </summary>
    public static RepeatingRows Empty { get; } = new();

    /// <summary>
    /// The rows, in the order the respondent sees them.
    /// </summary>
    public IReadOnlyList<RepeatingRow> Rows
    {
        get => _rows ?? [];
        init => _rows = value is null ? null : Array.AsReadOnly<RepeatingRow>([.. value]);
    }

    /// <summary>
    /// Appends a fresh, empty row with a newly generated <see cref="RepeatingRow.RowId"/>.
    /// </summary>
    /// <returns>
    /// A new <see cref="RepeatingRows"/> with the new row last.
    /// </returns>
    public RepeatingRows AddRow() =>
        this with { Rows = [.. Rows, new RepeatingRow { RowId = FormIds.NewRowId() }] };

    /// <summary>
    /// Removes the row with the given identifier.
    /// </summary>
    /// <param name="rowId">
    /// The identifier of the row to remove.
    /// </param>
    /// <returns>
    /// A new <see cref="RepeatingRows"/> without that row. Unchanged (as a new, equivalent
    /// instance) when no row carries <paramref name="rowId"/>.
    /// </returns>
    public RepeatingRows RemoveRow(string rowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rowId);

        return this with
        {
            Rows = [.. Rows.Where(row => !string.Equals(row.RowId, rowId, StringComparison.Ordinal))],
        };
    }

    /// <summary>
    /// Moves the row with the given identifier by <paramref name="delta"/> positions.
    /// </summary>
    /// <param name="rowId">
    /// The identifier of the row to move.
    /// </param>
    /// <param name="delta">
    /// The number of positions to move the row by — negative moves it earlier, positive moves it
    /// later. <c>-1</c> is "move up"; <c>1</c> is "move down".
    /// </param>
    /// <returns>
    /// A new <see cref="RepeatingRows"/> with the row relocated. Unchanged when
    /// <paramref name="rowId"/> is not found, or when the move would land the row outside the
    /// list — a bounds no-op, never a throw or a clamp, so a caller at either end can safely keep
    /// firing "move up"/"move down" without checking position first.
    /// </returns>
    public RepeatingRows MoveRow(string rowId, int delta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rowId);

        var index = IndexOf(rowId);

        if (index < 0)
        {
            return this;
        }

        var target = index + delta;

        if (target < 0 || target >= Rows.Count)
        {
            return this;
        }

        var reordered = new List<RepeatingRow>(Rows);
        var row = reordered[index];
        reordered.RemoveAt(index);
        reordered.Insert(target, row);

        return this with { Rows = reordered };
    }

    /// <summary>
    /// Sets one child's answer within one row.
    /// </summary>
    /// <param name="rowId">
    /// The identifier of the row to update.
    /// </param>
    /// <param name="childId">
    /// The identifier of the child node whose answer is changing.
    /// </param>
    /// <param name="value">
    /// The new answer.
    /// </param>
    /// <returns>
    /// A new <see cref="RepeatingRows"/> with that row's value updated. Unchanged when
    /// <paramref name="rowId"/> is not found.
    /// </returns>
    public RepeatingRows SetValue(string rowId, string childId, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(childId);

        var index = IndexOf(rowId);

        if (index < 0)
        {
            return this;
        }

        var updated = new List<RepeatingRow>(Rows) { [index] = Rows[index].SetValue(childId, value) };

        return this with { Rows = updated };
    }

    private int IndexOf(string rowId)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (string.Equals(Rows[i].RowId, rowId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Value equality over the ordered rows. The synthesized record equality would compare the
    /// backing list by reference, which makes two content-identical answers unequal — a trap for
    /// any consumer that diffs a recomputed answer against the stored one (the renderer's
    /// recompute loop, a designer preview). Rows are order-significant, so this is a positional
    /// sequence comparison, not a set comparison.
    /// </summary>
    public bool Equals(RepeatingRows? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Rows.Count != other.Rows.Count)
        {
            return false;
        }

        for (var i = 0; i < Rows.Count; i++)
        {
            if (!Rows[i].Equals(other.Rows[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Rows.Count);

        foreach (var row in Rows)
        {
            hash.Add(row.GetHashCode());
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// One row of a <see cref="RepeatingRows"/> answer: a stable identifier plus the row's own
/// answers, keyed by child node ID exactly like the top-level answer dictionary the expression
/// engine reads (PRD §5's "Reference semantics" — a bare node ID means the same thing whether it
/// is read from the outer flat answers or from a row's own).
/// </summary>
public sealed record RepeatingRow
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyValues =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<string, object?>? _values;

    /// <summary>
    /// The machine-generated, immutable identifier that keys this row, generated by
    /// <see cref="FormIds.NewRowId"/>. Never derived from the row's own answers.
    /// </summary>
    public required string RowId { get; init; }

    /// <summary>
    /// The row's own answers, keyed by child node ID.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Values
    {
        get => _values ?? EmptyValues;
        init => _values = value is null ? null : new Dictionary<string, object?>(value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Sets one child's answer.
    /// </summary>
    /// <param name="childId">
    /// The identifier of the child node whose answer is changing.
    /// </param>
    /// <param name="value">
    /// The new answer.
    /// </param>
    /// <returns>
    /// A new <see cref="RepeatingRow"/> carrying the same <see cref="RowId"/> with
    /// <see cref="Values"/> updated.
    /// </returns>
    public RepeatingRow SetValue(string childId, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childId);

        var updated = new Dictionary<string, object?>(Values, StringComparer.Ordinal) { [childId] = value };

        return this with { Values = updated };
    }

    /// <summary>
    /// Value equality over <see cref="RowId"/> and every answer in <see cref="Values"/>. The
    /// synthesized record equality compares the backing dictionary by reference; this compares it
    /// by content, treating two collection answers (a checkbox group's selections) as equal when
    /// their elements match in order. A nested <see cref="RepeatingRows"/> answer composes through
    /// its own value equality.
    /// </summary>
    public bool Equals(RepeatingRow? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (!string.Equals(RowId, other.RowId, StringComparison.Ordinal) || Values.Count != other.Values.Count)
        {
            return false;
        }

        foreach (var pair in Values)
        {
            if (!other.Values.TryGetValue(pair.Key, out var otherValue) || !AnswerEquals(pair.Value, otherValue))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(RowId, Values.Count);

    private static bool AnswerEquals(object? a, object? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        if (a is string leftText && b is string rightText)
        {
            return string.Equals(leftText, rightText, StringComparison.Ordinal);
        }

        if (a is IEnumerable<string> leftItems && b is IEnumerable<string> rightItems)
        {
            return leftItems.SequenceEqual(rightItems, StringComparer.Ordinal);
        }

        return a.Equals(b);
    }
}
