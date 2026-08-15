using BlazeForms.Definitions;
using BlazeForms.Expressions;

namespace BlazeForms.Core.Tests;

/// <summary>
/// The calc evaluator's behaviour is an author-facing contract (PRD §5, §13): the blank-and-error
/// policy, operand order, the injected <c>today</c>, and the topological ordering that lets one
/// calculation depend on another. These are pinned here.
/// </summary>
public sealed class CalcEvaluatorTests
{
    private static readonly DateOnly AnyToday = new(2026, 8, 15);

    private static Dictionary<string, object?> Values(params (string Key, object? Value)[] pairs)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            values[key] = value;
        }

        return values;
    }

    private static CalcExpression Expression(CalcOperation operation, params CalcOperand[] operands) =>
        new() { Operation = operation, Operands = operands };

    private static CalcOperand Field(string id) => new() { Field = id };

    private static CalcOperand Number(decimal value) => new() { Number = value };

    [Fact]
    public void SumAddsEveryNumericOperand()
    {
        var expression = Expression(CalcOperation.Sum, Field("a"), Field("b"), Number(50m));

        var result = CalcEvaluator.Evaluate(expression, Values(("a", 10m), ("b", 5m)), AnyToday);

        Assert.Equal(65m, result);
    }

    [Fact]
    public void SumSkipsBlankOperandsRatherThanTreatingThemAsZero()
    {
        var expression = Expression(CalcOperation.Sum, Field("a"), Field("missing"), Number(5m));

        var result = CalcEvaluator.Evaluate(expression, Values(("a", 10m)), AnyToday);

        Assert.Equal(15m, result);
    }

    [Fact]
    public void SumOfAllBlankOperandsIsNoValueNotZero()
    {
        var expression = Expression(CalcOperation.Sum, Field("a"), Field("b"));

        var result = CalcEvaluator.Evaluate(expression, Values(("a", ""), ("b", "   ")), AnyToday);

        Assert.Null(result);
    }

    [Fact]
    public void SubtractFoldsLeftInOperandOrder()
    {
        var expression = Expression(CalcOperation.Subtract, Number(100m), Number(30m), Number(20m));

        var result = CalcEvaluator.Evaluate(expression, Values(), AnyToday);

        Assert.Equal(50m, result);
    }

    [Fact]
    public void MultiplyFoldsEveryOperand()
    {
        var expression = Expression(CalcOperation.Multiply, Field("qty"), Field("price"));

        var result = CalcEvaluator.Evaluate(expression, Values(("qty", 3m), ("price", 4.5m)), AnyToday);

        Assert.Equal(13.5m, result);
    }

    [Fact]
    public void SubtractWithABlankOperandIsNoValue()
    {
        var expression = Expression(CalcOperation.Subtract, Number(100m), Field("missing"));

        var result = CalcEvaluator.Evaluate(expression, Values(), AnyToday);

        Assert.Null(result);
    }

    [Fact]
    public void DivideComputesLeftFold()
    {
        var expression = Expression(CalcOperation.Divide, Number(100m), Number(4m), Number(5m));

        var result = CalcEvaluator.Evaluate(expression, Values(), AnyToday);

        Assert.Equal(5m, result);
    }

    [Fact]
    public void DivideByZeroIsNoValueRatherThanAThrow()
    {
        var expression = Expression(CalcOperation.Divide, Number(100m), Number(0m));

        var result = CalcEvaluator.Evaluate(expression, Values(), AnyToday);

        Assert.Null(result);
    }

    [Fact]
    public void ANumberStoredAsAnIsoStringFromADraftStillCoerces()
    {
        var expression = Expression(CalcOperation.Sum, Field("a"), Field("b"));

        var result = CalcEvaluator.Evaluate(expression, Values(("a", "21.0"), ("b", "4")), AnyToday);

        Assert.Equal(25m, result);
    }

    [Fact]
    public void ATextAnswerThatIsNotANumberMakesTheExpressionNoValue()
    {
        var expression = Expression(CalcOperation.Sum, Field("a"), Number(5m));

        var result = CalcEvaluator.Evaluate(expression, Values(("a", "not a number")), AnyToday);

        Assert.Null(result);
    }

    [Fact]
    public void TodayResolvesToTheSuppliedDateNeverAClock()
    {
        var expression = Expression(CalcOperation.DateAddDays, new CalcOperand { Function = CalcFunction.Today }, Number(7m));

        var result = CalcEvaluator.Evaluate(expression, Values(), new DateOnly(2026, 1, 1));

        Assert.Equal(new DateOnly(2026, 1, 8), result);
    }

    [Fact]
    public void DateAddDaysAdvancesADateByAWholeNumberOfDays()
    {
        var expression = Expression(CalcOperation.DateAddDays, Field("start"), Number(30m));

        var result = CalcEvaluator.Evaluate(expression, Values(("start", new DateOnly(2026, 2, 15))), AnyToday);

        Assert.Equal(new DateOnly(2026, 3, 17), result);
    }

    [Fact]
    public void DateAddDaysIsImmuneToDaylightSavingBecauseItWorksInDateOnly()
    {
        // A naive DateTime-based add across a spring-forward boundary can land on the wrong day; the
        // DateOnly arithmetic here cannot.
        var expression = Expression(CalcOperation.DateAddDays, Field("start"), Number(1m));

        var result = CalcEvaluator.Evaluate(expression, Values(("start", new DateOnly(2026, 3, 8))), AnyToday);

        Assert.Equal(new DateOnly(2026, 3, 9), result);
    }

    [Fact]
    public void DateDiffDaysCountsFromTheFirstDateToTheSecond()
    {
        var expression = Expression(CalcOperation.DateDiffDays, Field("from"), Field("to"));

        var result = CalcEvaluator.Evaluate(
            expression,
            Values(("from", new DateOnly(2026, 1, 1)), ("to", new DateOnly(2026, 1, 31))),
            AnyToday);

        Assert.Equal(30m, result);
    }

    [Fact]
    public void ADateOperationWithTheWrongOperandCountIsNoValue()
    {
        var expression = Expression(CalcOperation.DateAddDays, Field("start"));

        var result = CalcEvaluator.Evaluate(expression, Values(("start", new DateOnly(2026, 1, 1))), AnyToday);

        Assert.Null(result);
    }

    [Fact]
    public void AnOperandThatSetsMoreThanOneMemberIsTreatedAsBlank()
    {
        var malformed = new CalcOperand { Field = "a", Number = 5m };
        var expression = Expression(CalcOperation.Sum, malformed, Number(10m));

        // The malformed operand contributes nothing; the well-formed literal still sums.
        var result = CalcEvaluator.Evaluate(expression, Values(("a", 99m)), AnyToday);

        Assert.Equal(10m, result);
    }

    [Fact]
    public void DateAddDaysWithADayCountOutsideIntRangeIsNoValueNotAThrow()
    {
        var expression = Expression(CalcOperation.DateAddDays, Field("start"), Number(decimal.MaxValue));

        var result = CalcEvaluator.Evaluate(expression, Values(("start", new DateOnly(2026, 1, 1))), AnyToday);

        Assert.Null(result);
    }

    [Fact]
    public void DateAddDaysThatWouldLeaveTheSupportedYearRangeIsNoValueNotAThrow()
    {
        var expression = Expression(CalcOperation.DateAddDays, Field("start"), Number(3_000_000m));

        var result = CalcEvaluator.Evaluate(expression, Values(("start", new DateOnly(2026, 1, 1))), AnyToday);

        Assert.Null(result);
    }

    [Fact]
    public void ASumThatOverrunsDecimalRangeIsNoValueNotAThrow()
    {
        var expression = Expression(CalcOperation.Sum, Number(decimal.MaxValue), Number(decimal.MaxValue));

        var result = CalcEvaluator.Evaluate(expression, Values(), AnyToday);

        Assert.Null(result);
    }

    [Fact]
    public void AMultiplyThatOverrunsDecimalRangeIsNoValueNotAThrow()
    {
        var expression = Expression(CalcOperation.Multiply, Number(decimal.MaxValue), Number(1000m));

        var result = CalcEvaluator.Evaluate(expression, Values(), AnyToday);

        Assert.Null(result);
    }

    [Fact]
    public void ADivisionWhoseResultOverrunsDecimalRangeIsNoValueNotAThrow()
    {
        var expression = Expression(CalcOperation.Divide, Number(decimal.MaxValue), Number(0.0001m));

        var result = CalcEvaluator.Evaluate(expression, Values(), AnyToday);

        Assert.Null(result);
    }

    [Fact]
    public void ANonFiniteDoubleAnswerIsTreatedAsNotANumber()
    {
        var expression = Expression(CalcOperation.Sum, Field("a"), Number(5m));

        var result = CalcEvaluator.Evaluate(expression, Values(("a", double.PositiveInfinity)), AnyToday);

        Assert.Null(result);
    }

    [Fact]
    public void EvaluateGuardsAgainstNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CalcEvaluator.Evaluate(null!, Values(), AnyToday));
        Assert.Throws<ArgumentNullException>(() =>
            CalcEvaluator.Evaluate(Expression(CalcOperation.Sum), null!, AnyToday));
    }

    // ---- EvaluateAll: topological ordering across a whole definition ----

    private static FormDefinition WithCalcNodes(params FormNode[] nodes) => new()
    {
        Id = "form",
        Name = "Form",
        Pages =
        [
            new FormPage
            {
                Id = "p",
                Title = "P",
                Sections = [new FormSection { Id = "s", Title = "S", Nodes = nodes }],
            },
        ],
    };

    [Fact]
    public void EvaluateAllReturnsAnEntryForEveryCalcNodeThatCarriesACalculation()
    {
        var definition = WithCalcNodes(
            new FormNode { Id = "fee", Type = NodeType.Currency, Label = "Fee" },
            new FormNode
            {
                Id = "total",
                Type = NodeType.Calc,
                Label = "Total",
                Calculation = Expression(CalcOperation.Sum, Field("fee"), Number(50m)),
            },
            new FormNode { Id = "no-calc", Type = NodeType.Calc, Label = "Empty calc" });

        var results = CalcEvaluator.EvaluateAll(definition, Values(("fee", 100m)), AnyToday);

        Assert.True(results.ContainsKey("total"));
        Assert.False(results.ContainsKey("no-calc"));
        Assert.Equal(150m, results["total"]);
    }

    [Fact]
    public void ACalcNodeCanDependOnAnotherCalcNodeRegardlessOfDeclarationOrder()
    {
        // 'grand-total' is declared before the 'subtotal' it depends on; EvaluateAll must still
        // compute subtotal first.
        var definition = WithCalcNodes(
            new FormNode { Id = "fee", Type = NodeType.Currency, Label = "Fee" },
            new FormNode
            {
                Id = "grand-total",
                Type = NodeType.Calc,
                Label = "Grand total",
                Calculation = Expression(CalcOperation.Multiply, Field("subtotal"), Number(2m)),
            },
            new FormNode
            {
                Id = "subtotal",
                Type = NodeType.Calc,
                Label = "Subtotal",
                Calculation = Expression(CalcOperation.Sum, Field("fee"), Number(10m)),
            });

        var results = CalcEvaluator.EvaluateAll(definition, Values(("fee", 5m)), AnyToday);

        Assert.Equal(15m, results["subtotal"]);
        Assert.Equal(30m, results["grand-total"]);
    }

    [Fact]
    public void EveryCalcNodeInACycleEvaluatesToNull()
    {
        // a -> b -> a. Only reachable through an imported definition; the designer rejects it.
        var definition = WithCalcNodes(
            new FormNode
            {
                Id = "a",
                Type = NodeType.Calc,
                Label = "A",
                Calculation = Expression(CalcOperation.Sum, Field("b"), Number(1m)),
            },
            new FormNode
            {
                Id = "b",
                Type = NodeType.Calc,
                Label = "B",
                Calculation = Expression(CalcOperation.Sum, Field("a"), Number(1m)),
            });

        var results = CalcEvaluator.EvaluateAll(definition, Values(), AnyToday);

        Assert.Null(results["a"]);
        Assert.Null(results["b"]);
    }

    [Fact]
    public void ADirectSelfReferenceEvaluatesToNull()
    {
        var definition = WithCalcNodes(
            new FormNode
            {
                Id = "a",
                Type = NodeType.Calc,
                Label = "A",
                Calculation = Expression(CalcOperation.Sum, Field("a"), Number(1m)),
            });

        var results = CalcEvaluator.EvaluateAll(definition, Values(), AnyToday);

        Assert.Null(results["a"]);
    }

    [Fact]
    public void ANodeDependingOnACyclicNodeStillComputesReadingTheCyclicValueAsBlank()
    {
        // a <-> b is a cycle; c depends on a. c is not itself cyclic, so it computes, reading a's
        // null as a skipped blank.
        var definition = WithCalcNodes(
            new FormNode
            {
                Id = "a",
                Type = NodeType.Calc,
                Label = "A",
                Calculation = Expression(CalcOperation.Sum, Field("b")),
            },
            new FormNode
            {
                Id = "b",
                Type = NodeType.Calc,
                Label = "B",
                Calculation = Expression(CalcOperation.Sum, Field("a")),
            },
            new FormNode
            {
                Id = "c",
                Type = NodeType.Calc,
                Label = "C",
                Calculation = Expression(CalcOperation.Sum, Field("a"), Number(7m)),
            });

        var results = CalcEvaluator.EvaluateAll(definition, Values(), AnyToday);

        Assert.Null(results["a"]);
        Assert.Null(results["b"]);
        Assert.Equal(7m, results["c"]);
    }

    [Fact]
    public void EvaluateAllNeverMutatesTheSuppliedValues()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal) { ["fee"] = 100m };
        var definition = WithCalcNodes(
            new FormNode { Id = "fee", Type = NodeType.Currency, Label = "Fee" },
            new FormNode
            {
                Id = "total",
                Type = NodeType.Calc,
                Label = "Total",
                Calculation = Expression(CalcOperation.Sum, Field("fee")),
            });

        _ = CalcEvaluator.EvaluateAll(definition, values, AnyToday);

        Assert.Single(values);
        Assert.False(values.ContainsKey("total"));
    }
}
