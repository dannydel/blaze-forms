using BlazeForms.Serialization;

namespace BlazeForms.Core.Tests;

/// <summary>
/// <see cref="RepeatingRows"/>/<see cref="RepeatingRow"/> are values: every mutator returns a new
/// instance rather than changing the one it was called on (AGENTS.md invariant #5).
/// </summary>
public sealed class RepeatingRowsTests
{
    [Fact]
    public void AddRowAppendsANewRowWithoutMutatingTheOriginal()
    {
        var original = RepeatingRows.Empty;

        var updated = original.AddRow();

        Assert.Empty(original.Rows);
        Assert.Single(updated.Rows);
        Assert.NotEmpty(updated.Rows[0].RowId);
        Assert.Empty(updated.Rows[0].Values);
    }

    [Fact]
    public void AddRowGeneratesADistinctRowIdEachTime()
    {
        var rows = RepeatingRows.Empty.AddRow().AddRow();

        Assert.Equal(2, rows.Rows.Count);
        Assert.NotEqual(rows.Rows[0].RowId, rows.Rows[1].RowId);
    }

    [Fact]
    public void RemoveRowDropsOnlyTheMatchingRowWithoutMutatingTheOriginal()
    {
        var original = RepeatingRows.Empty.AddRow().AddRow();
        var keptId = original.Rows[1].RowId;

        var updated = original.RemoveRow(original.Rows[0].RowId);

        Assert.Equal(2, original.Rows.Count);
        Assert.Single(updated.Rows);
        Assert.Equal(keptId, updated.Rows[0].RowId);
    }

    [Fact]
    public void RemoveRowWithAnUnknownIdIsANoOp()
    {
        var original = RepeatingRows.Empty.AddRow();

        var updated = original.RemoveRow("row-does-not-exist");

        Assert.Equal(original.Rows.Select(row => row.RowId), updated.Rows.Select(row => row.RowId));
    }

    [Fact]
    public void MoveRowRelocatesTheRowByDeltaWithoutMutatingTheOriginal()
    {
        var original = RepeatingRows.Empty.AddRow().AddRow().AddRow();
        var ids = original.Rows.Select(row => row.RowId).ToList();

        var updated = original.MoveRow(ids[0], 1);

        Assert.Equal(ids, original.Rows.Select(row => row.RowId));
        Assert.Equal([ids[1], ids[0], ids[2]], updated.Rows.Select(row => row.RowId));
    }

    [Fact]
    public void MoveRowUpMovesTheRowEarlier()
    {
        var original = RepeatingRows.Empty.AddRow().AddRow();
        var ids = original.Rows.Select(row => row.RowId).ToList();

        var updated = original.MoveRow(ids[1], -1);

        Assert.Equal([ids[1], ids[0]], updated.Rows.Select(row => row.RowId));
    }

    [Fact]
    public void MoveRowPastTheStartIsANoOp()
    {
        var original = RepeatingRows.Empty.AddRow().AddRow();
        var ids = original.Rows.Select(row => row.RowId).ToList();

        var updated = original.MoveRow(ids[0], -1);

        Assert.Equal(ids, updated.Rows.Select(row => row.RowId));
    }

    [Fact]
    public void MoveRowPastTheEndIsANoOp()
    {
        var original = RepeatingRows.Empty.AddRow().AddRow();
        var ids = original.Rows.Select(row => row.RowId).ToList();

        var updated = original.MoveRow(ids[1], 1);

        Assert.Equal(ids, updated.Rows.Select(row => row.RowId));
    }

    [Fact]
    public void MoveRowWithAnUnknownIdIsANoOp()
    {
        var original = RepeatingRows.Empty.AddRow();

        var updated = original.MoveRow("row-does-not-exist", 1);

        Assert.Equal(original.Rows.Select(row => row.RowId), updated.Rows.Select(row => row.RowId));
    }

    [Fact]
    public void SetValueUpdatesOnlyTheNamedRowWithoutMutatingTheOriginal()
    {
        var original = RepeatingRows.Empty.AddRow().AddRow();
        var firstRowId = original.Rows[0].RowId;
        var secondRowId = original.Rows[1].RowId;

        var updated = original.SetValue(firstRowId, "child-name", "Ada");

        Assert.Empty(original.Rows[0].Values);
        Assert.Equal("Ada", updated.Rows[0].Values["child-name"]);
        Assert.Empty(updated.Rows[1].Values);
        Assert.Equal(firstRowId, updated.Rows[0].RowId);
        Assert.Equal(secondRowId, updated.Rows[1].RowId);
    }

    [Fact]
    public void SetValueWithAnUnknownRowIdIsANoOp()
    {
        var original = RepeatingRows.Empty.AddRow();

        var updated = original.SetValue("row-does-not-exist", "child-name", "Ada");

        Assert.Empty(updated.Rows[0].Values);
    }

    [Fact]
    public void SetValueOverwritesAnExistingChildValue()
    {
        var row = new RepeatingRows().AddRow();
        var rowId = row.Rows[0].RowId;

        var updated = row.SetValue(rowId, "child-name", "Ada").SetValue(rowId, "child-name", "Grace");

        Assert.Equal("Grace", updated.Rows[0].Values["child-name"]);
    }

    [Fact]
    public void RowSetValueReturnsANewRowCarryingTheSameRowId()
    {
        var row = new RepeatingRow { RowId = "row-1" };

        var updated = row.SetValue("child-name", "Ada");

        Assert.Empty(row.Values);
        Assert.Equal("row-1", updated.RowId);
        Assert.Equal("Ada", updated.Values["child-name"]);
    }

    [Fact]
    public void ConstructingARowWithValuesCopiesRatherThanAliasesTheSource()
    {
        var source = new Dictionary<string, object?>(StringComparer.Ordinal) { ["child-name"] = "Ada" };
        var row = new RepeatingRow { RowId = "row-1", Values = source };

        source["child-name"] = "Mutated after construction";

        Assert.Equal("Ada", row.Values["child-name"]);
    }

    // -- Value equality (the record's synthesized equality would compare collections by reference) --

    private static RepeatingRow Row(string rowId, params (string Key, object? Value)[] values)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            dict[key] = value;
        }

        return new RepeatingRow { RowId = rowId, Values = dict };
    }

    [Fact]
    public void TwoContentIdenticalRowsAreEqualAndHashTheSame()
    {
        var a = Row("row-1", ("name", "Ada"), ("grade", 9m));
        var b = Row("row-1", ("name", "Ada"), ("grade", 9m));

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void RowsDifferingInARowIdOrAValueAreNotEqual()
    {
        var baseline = Row("row-1", ("name", "Ada"));

        Assert.NotEqual(baseline, Row("row-2", ("name", "Ada")));
        Assert.NotEqual(baseline, Row("row-1", ("name", "Grace")));
        Assert.NotEqual(baseline, Row("row-1", ("name", "Ada"), ("extra", 1m)));
    }

    [Fact]
    public void RowCollectionAnswersCompareByElementsNotReference()
    {
        string[] both = ["lift", "aide"];
        string[] one = ["lift"];
        var a = Row("row-1", ("choices", both));
        var b = Row("row-1", ("choices", new List<string> { "lift", "aide" }));
        var different = Row("row-1", ("choices", one));

        Assert.Equal(a, b);
        Assert.NotEqual(a, different);
    }

    [Fact]
    public void TwoContentIdenticalRepeatingRowsAreEqualAndHashTheSame()
    {
        var a = new RepeatingRows { Rows = [Row("row-1", ("name", "Ada")), Row("row-2", ("name", "Grace"))] };
        var b = new RepeatingRows { Rows = [Row("row-1", ("name", "Ada")), Row("row-2", ("name", "Grace"))] };

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void EmptyRepeatingRowsEqualsTheEmptySingleton()
    {
        Assert.Equal(RepeatingRows.Empty, new RepeatingRows { Rows = [] });
        Assert.Equal(RepeatingRows.Empty, new RepeatingRows());
    }

    [Fact]
    public void RowOrderIsSignificantForRepeatingRowsEquality()
    {
        var a = new RepeatingRows { Rows = [Row("row-1"), Row("row-2")] };
        var reordered = new RepeatingRows { Rows = [Row("row-2"), Row("row-1")] };

        Assert.NotEqual(a, reordered);
    }

    [Fact]
    public void SetValueToAnEqualValueProducesAnEqualInstance()
    {
        // The renderer's recompute writes EvaluateAll output back and diffs it against the stored
        // answer; an unchanged recompute must compare equal so it does not churn.
        var original = RepeatingRows.Empty.AddRow();
        var rowId = original.Rows[0].RowId;
        var withValue = original.SetValue(rowId, "total", 42m);

        var recomputedSame = withValue.SetValue(rowId, "total", 42m);

        Assert.Equal(withValue, recomputedSame);
    }
}
