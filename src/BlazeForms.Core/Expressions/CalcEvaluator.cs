using BlazeForms.Definitions;

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
    /// A map from calc node ID to its computed value, holding an entry for every calc node that
    /// carries a <see cref="FormNode.Calculation"/>. This is the renderer's single evaluation entry
    /// point: it writes these back into its own answer store so dependent calculations, visibility
    /// rules, and validation all see them.
    /// </returns>
    public static IReadOnlyDictionary<string, object?> EvaluateAll(
        FormDefinition definition,
        IReadOnlyDictionary<string, object?> values,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);

        var calcNodes = definition.EnumerateNodes()
            .Where(node => node.Type == NodeType.Calc && node.Calculation is not null)
            .ToList();

        var results = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (calcNodes.Count == 0)
        {
            return results;
        }

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

        var working = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            working[pair.Key] = pair.Value;
        }

        // A calc node that can reach itself through calc-to-calc references is part of a cycle. It,
        // and only it, evaluates to null; nodes that merely depend on a cyclic node read that null
        // as a blank operand and compute normally.
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in byId.Keys)
        {
            if (CanReach(id, id, dependencies, new HashSet<string>(StringComparer.Ordinal)))
            {
                results[id] = null;
                working[id] = null;
                done.Add(id);
            }
        }

        foreach (var id in byId.Keys)
        {
            Visit(id, byId, dependencies, working, results, done, today);
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
