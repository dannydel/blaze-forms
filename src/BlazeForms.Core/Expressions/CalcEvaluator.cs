using BlazeForms.Definitions;
using BlazeForms.Serialization;

namespace BlazeForms.Expressions;

/// <summary>
/// Evaluates a <see cref="CalcExpression"/> against a respondent's answers (PRD §5, §13). The
/// value-valued counterpart of <see cref="ConditionEvaluator"/>: where that decides a boolean, this
/// computes a number or a date for a <see cref="NodeType.Calc"/> node.
/// </summary>
/// <remarks>
/// <para>
/// Every method here is pure and deterministic. The one piece of ambient state a calculation could
/// want — today's date, for <see cref="CalcFunction.Today"/> — is a parameter, never a clock read,
/// so a captured submission recomputes to the same value forever and a test can pin any date it
/// likes.
/// </para>
/// <para>
/// The blank-and-error policy is the author-predictability contract. A <see cref="CalcOperation.Sum"/>
/// skips blank operands and reads all-blank as no value rather than as zero, since zero would look
/// like a real answer. Every other operation treats any blank or non-coercible operand — and a
/// division by zero — as making the whole expression evaluate to <see langword="null"/>, which the
/// renderer shows as the node's placeholder. Nothing here throws on bad data: a malformed operand
/// (one that sets no member, or several) is simply no value, exactly as a dangling field reference
/// is a lint (FR-03) rather than a runtime error.
/// </para>
/// </remarks>
public static class CalcEvaluator
{
    /// <summary>
    /// Evaluates a single calculation expression.
    /// </summary>
    /// <param name="expression">
    /// The expression to evaluate.
    /// </param>
    /// <param name="values">
    /// The respondent's answers, keyed by node ID. A calc node's own computed value must already be
    /// present here for another calculation to read it — <see cref="EvaluateAll"/> arranges that;
    /// this method reads <paramref name="values"/> exactly as given.
    /// </param>
    /// <param name="today">
    /// The date <see cref="CalcFunction.Today"/> resolves to.
    /// </param>
    /// <returns>
    /// A <see cref="decimal"/>, a <see cref="DateOnly"/>, or <see langword="null"/> when the
    /// expression has no value.
    /// </returns>
    public static object? Evaluate(CalcExpression expression, IReadOnlyDictionary<string, object?> values, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(values);

        var operands = expression.Operands;

        return expression.Operation switch
        {
            CalcOperation.Sum => (object?)Sum(operands, values, today),
            CalcOperation.Subtract => (object?)FoldNumbers(operands, values, today, static (running, next) => running - next),
            CalcOperation.Multiply => (object?)FoldNumbers(operands, values, today, static (running, next) => running * next),
            CalcOperation.Divide => (object?)Divide(operands, values, today),
            CalcOperation.DateAddDays => (object?)DateAddDays(operands, values, today),
            CalcOperation.DateDiffDays => (object?)DateDiffDays(operands, values, today),
            _ => null,
        };
    }

    /// <summary>
    /// Computes every <see cref="NodeType.Calc"/> node's value across a whole definition, in an
    /// order that lets one calculation depend on another. A calc node that references another calc
    /// node is evaluated after it; every node caught in a reference cycle — reachable only through
    /// an imported definition, since the designer rejects a cycle as it is authored — evaluates to
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="definition">
    /// The definition whose calc nodes to compute.
    /// </param>
    /// <param name="values">
    /// The respondent's answers, keyed by node ID. Never mutated.
    /// </param>
    /// <param name="today">
    /// The date <see cref="CalcFunction.Today"/> resolves to.
    /// </param>
    /// <returns>
    /// A map keyed by node ID, holding one entry for every top-level calc node that carries a
    /// <see cref="FormNode.Calculation"/>, plus one entry per repeating group whose children
    /// include at least one calc node with an existing row to compute — keyed by the group's own
    /// ID, carrying an updated <see cref="RepeatingRows"/> with each row's calc children
    /// recomputed. A form with no repeating groups gets exactly today's flat result. This is the
    /// renderer's single evaluation entry point: it writes the top-level entries back into its own
    /// answer store, and the group entries back as that group's whole value, so dependent
    /// calculations, visibility rules, and validation all see them.
    /// </returns>
    public static IReadOnlyDictionary<string, object?> EvaluateAll(
        FormDefinition definition,
        IReadOnlyDictionary<string, object?> values,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);

        var topLevelCalcNodes = new List<FormNode>();
        var groupsWithCalcChildren = new List<(FormNode Group, List<FormNode> CalcChildren)>();

        foreach (var section in definition.Pages.SelectMany(page => page.Sections))
        {
            CollectCalcNodes(section.Nodes, topLevelCalcNodes, groupsWithCalcChildren);
        }

        var results = EvaluateAllInGraph(BuildGraph(topLevelCalcNodes), values, today);

        if (groupsWithCalcChildren.Count == 0)
        {
            return results;
        }

        // Row calculations may read a top-level calc result (an outer→row reference is allowed;
        // it is only the reverse, a rule outside the group reading into a row, that FR-04 blocks),
        // so the outer context every row merges against carries this pass's top-level results.
        var outerContext = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        foreach (var pair in results)
        {
            outerContext[pair.Key] = pair.Value;
        }

        foreach (var (group, calcChildren) in groupsWithCalcChildren)
        {
            if (!values.TryGetValue(group.Id, out var raw) || raw is not RepeatingRows rows || rows.Rows.Count == 0)
            {
                continue;
            }

            // Rows share the same child node IDs, so the dependency/cycle graph is identical for
            // every row; it is built once here rather than once per row.
            var groupGraph = BuildGraph(calcChildren);
            var updatedRows = new List<RepeatingRow>(rows.Rows.Count);
            var rowChanged = false;

            foreach (var row in rows.Rows)
            {
                var rowResults = EvaluateAllInGraph(groupGraph, RowScope.Merge(outerContext, row), today);

                if (rowResults.Count == 0)
                {
                    updatedRows.Add(row);
                    continue;
                }

                var updatedValues = new Dictionary<string, object?>(row.Values, StringComparer.Ordinal);
                foreach (var pair in rowResults)
                {
                    updatedValues[pair.Key] = pair.Value;
                }

                updatedRows.Add(row with { Values = updatedValues });
                rowChanged = true;
            }

            if (rowChanged)
            {
                results[group.Id] = rows with { Rows = updatedRows };
            }
        }

        return results;
    }

    /// <summary>
    /// Separates a node list into the top-level calc nodes (outside any repeating group) and, for
    /// each repeating group encountered, the calc nodes among its own children. A repeating
    /// group's children are never folded into <paramref name="topLevelCalcNodes"/> and are never
    /// walked any deeper (PRD's "one nesting level this slice") — each group's calc children are
    /// evaluated per row by <see cref="EvaluateAll"/>, not against the flat top-level graph.
    /// </summary>
    private static void CollectCalcNodes(
        IReadOnlyList<FormNode> nodes,
        List<FormNode> topLevelCalcNodes,
        List<(FormNode Group, List<FormNode> CalcChildren)> groupsWithCalcChildren)
    {
        foreach (var node in nodes)
        {
            if (node.Type == NodeType.Repeating)
            {
                var calcChildren = node.Children
                    .Where(child => child.Type == NodeType.Calc && child.Calculation is not null)
                    .ToList();

                if (calcChildren.Count > 0)
                {
                    groupsWithCalcChildren.Add((node, calcChildren));
                }

                continue;
            }

            if (node.Type == NodeType.Calc && node.Calculation is not null)
            {
                topLevelCalcNodes.Add(node);
            }

            if (node.Children.Count > 0)
            {
                CollectCalcNodes(node.Children, topLevelCalcNodes, groupsWithCalcChildren);
            }
        }
    }

    /// <summary>
    /// The dependency/cycle information <see cref="EvaluateAllInGraph"/> needs, computed once for
    /// a fixed set of calc nodes and reused across however many answer sets evaluate against it —
    /// once for the top-level graph, and once per repeating group (shared by every one of that
    /// group's rows, since they all share the same child node IDs).
    /// </summary>
    private sealed record CalcGraph(
        Dictionary<string, CalcExpression> ById,
        Dictionary<string, IReadOnlyList<string>> Dependencies,
        HashSet<string> Cyclic);

    private static CalcGraph BuildGraph(IReadOnlyList<FormNode> calcNodes)
    {
        var byId = new Dictionary<string, CalcExpression>(StringComparer.Ordinal);
        foreach (var node in calcNodes)
        {
            // A definition with two nodes sharing an ID is malformed; the first one wins here, and
            // the linter reports the duplicate separately. Never throw on untrusted input.
            byId.TryAdd(node.Id, node.Calculation!);
        }

        var dependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (id, expression) in byId)
        {
            dependencies[id] = expression.Operands
                .Where(operand => operand.Field is not null && byId.ContainsKey(operand.Field))
                .Select(operand => operand.Field!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        // A calc node that can reach itself through calc-to-calc references is part of a cycle. It,
        // and only it, evaluates to null; nodes that merely depend on a cyclic node read that null
        // as a blank operand and compute normally.
        var cyclic = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in byId.Keys)
        {
            if (CanReach(id, id, dependencies, new HashSet<string>(StringComparer.Ordinal)))
            {
                cyclic.Add(id);
            }
        }

        return new CalcGraph(byId, dependencies, cyclic);
    }

    private static Dictionary<string, object?> EvaluateAllInGraph(
        CalcGraph graph,
        IReadOnlyDictionary<string, object?> values,
        DateOnly today)
    {
        var results = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (graph.ById.Count == 0)
        {
            return results;
        }

        var working = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in graph.ById.Keys)
        {
            if (graph.Cyclic.Contains(id))
            {
                results[id] = null;
                working[id] = null;
                done.Add(id);
            }
        }

        foreach (var id in graph.ById.Keys)
        {
            Visit(id, graph.ById, graph.Dependencies, working, results, done, today);
        }

        return results;
    }

    private static void Visit(
        string id,
        IReadOnlyDictionary<string, CalcExpression> byId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies,
        Dictionary<string, object?> working,
        Dictionary<string, object?> results,
        HashSet<string> done,
        DateOnly today)
    {
        if (done.Contains(id))
        {
            return;
        }

        foreach (var dependency in dependencies[id])
        {
            Visit(dependency, byId, dependencies, working, results, done, today);
        }

        var value = Evaluate(byId[id], working, today);
        results[id] = value;
        working[id] = value;
        done.Add(id);
    }

    private static bool CanReach(
        string from,
        string target,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies,
        HashSet<string> visited)
    {
        foreach (var next in dependencies[from])
        {
            if (string.Equals(next, target, StringComparison.Ordinal))
            {
                return true;
            }

            if (visited.Add(next) && CanReach(next, target, dependencies, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static decimal? Sum(IReadOnlyList<CalcOperand> operands, IReadOnlyDictionary<string, object?> values, DateOnly today)
    {
        var total = 0m;
        var contributed = false;

        foreach (var operand in operands)
        {
            var resolved = Resolve(operand, values, today);

            if (ValueCoercion.IsBlank(resolved))
            {
                continue;
            }

            if (!ValueCoercion.TryAsDecimal(resolved, out var number))
            {
                return null;
            }

            try
            {
                total += number;
            }
            catch (OverflowException)
            {
                // A running total that overruns decimal's range is no value, never a throw.
                return null;
            }

            contributed = true;
        }

        return contributed ? total : null;
    }

    private static decimal? FoldNumbers(
        IReadOnlyList<CalcOperand> operands,
        IReadOnlyDictionary<string, object?> values,
        DateOnly today,
        Func<decimal, decimal, decimal> combine)
    {
        decimal? running = null;

        foreach (var operand in operands)
        {
            var resolved = Resolve(operand, values, today);

            if (ValueCoercion.IsBlank(resolved) || !ValueCoercion.TryAsDecimal(resolved, out var number))
            {
                return null;
            }

            try
            {
                running = running is null ? number : combine(running.Value, number);
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        return running;
    }

    private static decimal? Divide(IReadOnlyList<CalcOperand> operands, IReadOnlyDictionary<string, object?> values, DateOnly today)
    {
        decimal? running = null;

        foreach (var operand in operands)
        {
            var resolved = Resolve(operand, values, today);

            if (ValueCoercion.IsBlank(resolved) || !ValueCoercion.TryAsDecimal(resolved, out var number))
            {
                return null;
            }

            if (running is null)
            {
                running = number;
            }
            else if (number == 0m)
            {
                return null;
            }
            else
            {
                try
                {
                    running /= number;
                }
                catch (OverflowException)
                {
                    return null;
                }
            }
        }

        return running;
    }

    private static DateOnly? DateAddDays(IReadOnlyList<CalcOperand> operands, IReadOnlyDictionary<string, object?> values, DateOnly today)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        var start = Resolve(operands[0], values, today);
        var offset = Resolve(operands[1], values, today);

        if (ValueCoercion.IsBlank(start) || ValueCoercion.IsBlank(offset))
        {
            return null;
        }

        if (!ValueCoercion.TryAsDateOnly(start, out var date) || !ValueCoercion.TryAsDecimal(offset, out var days))
        {
            return null;
        }

        // Respondent-controlled day counts must never crash the evaluator (it runs on every
        // keystroke): a count outside int range, or one that would push the date out of the 1–9999
        // year range DateOnly allows, is simply no value — the same policy as divide-by-zero.
        var truncated = decimal.Truncate(days);

        if (truncated < int.MinValue || truncated > int.MaxValue)
        {
            return null;
        }

        try
        {
            return date.AddDays((int)truncated);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static decimal? DateDiffDays(IReadOnlyList<CalcOperand> operands, IReadOnlyDictionary<string, object?> values, DateOnly today)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        var first = Resolve(operands[0], values, today);
        var second = Resolve(operands[1], values, today);

        if (ValueCoercion.IsBlank(first) || ValueCoercion.IsBlank(second))
        {
            return null;
        }

        if (!ValueCoercion.TryAsDateOnly(first, out var start) || !ValueCoercion.TryAsDateOnly(second, out var end))
        {
            return null;
        }

        return (decimal)(end.DayNumber - start.DayNumber);
    }

    /// <summary>
    /// Resolves one operand to the value it stands for: a field's stored answer, a literal number,
    /// or the supplied <paramref name="today"/>. An operand that does not set exactly one of its
    /// three members is malformed and resolves to <see langword="null"/> — a blank, not a throw.
    /// </summary>
    private static object? Resolve(CalcOperand operand, IReadOnlyDictionary<string, object?> values, DateOnly today)
    {
        var hasField = !string.IsNullOrWhiteSpace(operand.Field);
        var hasNumber = operand.Number.HasValue;
        var hasFunction = operand.Function.HasValue;

        if ((hasField ? 1 : 0) + (hasNumber ? 1 : 0) + (hasFunction ? 1 : 0) != 1)
        {
            return null;
        }

        if (hasField)
        {
            values.TryGetValue(operand.Field!, out var answer);
            return answer;
        }

        if (hasNumber)
        {
            return operand.Number!.Value;
        }

        return operand.Function switch
        {
            CalcFunction.Today => today,
            _ => null,
        };
    }
}
