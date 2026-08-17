using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Internal;
using BlazeForms.Serialization;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="CrossFieldValidator"/> in isolation: which rules fire, which are skipped,
/// how a target that already carries a per-field error is left alone (PRD §6), and — for a
/// row-scoped rule — that its merged view reads the settled outer values, never the raw ones,
/// so it never diverges from what a submit actually captures.
/// </summary>
public sealed class CrossFieldValidatorTests
{
    private static FormDefinition BuildDefinitionWithRule(ValidationRule rule) => new()
    {
        Id = "form-cross-field",
        Name = "Cross field",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-1",
                        Nodes =
                        [
                            new FormNode { Id = "is-employed", Type = NodeType.YesNo, Label = "Are you employed?" },
                            new FormNode { Id = "employer", Type = NodeType.Text, Label = "Employer" },
                        ],
                    },
                ],
            },
        ],
        ValidationRules = [rule],
    };

    private static readonly ValidationRule EmployerRule = new()
    {
        Target = "employer",
        Message = "Enter your employer's name for 'Employer'.",
        Expression = new ConditionGroup
        {
            Conditions =
            [
                new Condition { Field = "is-employed", Operator = ConditionOperator.Is, Value = "yes" },
                new Condition { Field = "employer", Operator = ConditionOperator.IsBlank },
            ],
        },
    };

    [Fact]
    public void ARuleWhoseExpressionDescribesTheInvalidStateAttachesItsMessageToItsTarget()
    {
        var definition = BuildDefinitionWithRule(EmployerRule);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal) { ["is-employed"] = "yes" };
        var visibleNodeIds = new HashSet<string>(StringComparer.Ordinal) { "is-employed", "employer" };
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        CrossFieldValidator.Evaluate(definition, values, values, visibleNodeIds, errors);

        Assert.Equal("Enter your employer's name for 'Employer'.", errors["employer"]);
    }

    [Fact]
    public void ARuleWhoseExpressionIsSatisfiedNeverAttachesAnything()
    {
        var definition = BuildDefinitionWithRule(EmployerRule);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["is-employed"] = "yes",
            ["employer"] = "Acme Corp",
        };
        var visibleNodeIds = new HashSet<string>(StringComparer.Ordinal) { "is-employed", "employer" };
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        CrossFieldValidator.Evaluate(definition, values, values, visibleNodeIds, errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void ARuleWhoseTargetIsHiddenIsSkippedEvenWhenItsExpressionWouldOtherwiseFire()
    {
        var definition = BuildDefinitionWithRule(EmployerRule);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal) { ["is-employed"] = "yes" };
        // "employer" is deliberately absent from the visible set -- e.g. hidden by its own
        // visibleWhen rule -- even though the values dictionary would otherwise make the rule's
        // expression true.
        var visibleNodeIds = new HashSet<string>(StringComparer.Ordinal) { "is-employed" };
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        CrossFieldValidator.Evaluate(definition, values, values, visibleNodeIds, errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void ARuleNeverOverwritesAFieldThatAlreadyCarriesAPerFieldError()
    {
        var definition = BuildDefinitionWithRule(EmployerRule);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal) { ["is-employed"] = "yes" };
        var visibleNodeIds = new HashSet<string>(StringComparer.Ordinal) { "is-employed", "employer" };
        var errors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["employer"] = "Enter a value for 'Employer'.",
        };

        CrossFieldValidator.Evaluate(definition, values, values, visibleNodeIds, errors);

        Assert.Equal("Enter a value for 'Employer'.", errors["employer"]);
    }

    /// <summary>
    /// Builds a one-group definition whose repeating child <c>confirm</c> is the target of a rule
    /// that reads an OUTER field, <c>outer-answer</c> — for proving <see cref="CrossFieldValidator"/>'s
    /// row-scoped path merges the row on top of whichever "outer values" dictionary it is handed,
    /// not always the same one <see cref="ARowScopedRuleReadsTheSettledOuterValuesNotTheRawOnes"/>
    /// and <see cref="ARowScopedRuleFiresWhenTheOuterFieldItReadsIsStillSettledVisible"/> both
    /// build their scenario from.
    /// </summary>
    private static (FormDefinition Definition, RepeatingRows Rows) BuildRowScopedDefinition()
    {
        var group = new FormNode
        {
            Id = "items",
            Type = NodeType.Repeating,
            Label = "Items",
            Children = [new FormNode { Id = "confirm", Type = NodeType.Text, Label = "Confirm" }],
        };
        var rule = new ValidationRule
        {
            Target = "confirm",
            Message = "'Outer answer' must not be 'secret'.",
            Expression = new ConditionGroup
            {
                Conditions = [new Condition { Field = "outer-answer", Operator = ConditionOperator.Is, Value = "secret" }],
            },
        };
        var definition = new FormDefinition
        {
            Id = "form-row-scoped",
            Name = "Row scoped",
            Pages = [new FormPage { Id = "page-1", Sections = [new FormSection { Id = "section-1", Nodes = [group] }] }],
            ValidationRules = [rule],
        };

        return (definition, RepeatingRows.Empty.AddRow());
    }

    [Fact]
    public void ARowScopedRuleReadsTheSettledOuterValuesNotTheRawOnes()
    {
        var (definition, rows) = BuildRowScopedDefinition();

        // "outer-answer" is stale in the raw store -- the field it once held now hides behind a
        // controller the respondent has since flipped -- but the settled dictionary this pass
        // actually resolved (the one BuildSubmissionEnvelope would capture from) has already
        // dropped it.
        var rawValues = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["outer-answer"] = "secret",
            ["items"] = rows,
        };
        var settledOuterValues = new Dictionary<string, object?>(StringComparer.Ordinal) { ["items"] = rows };
        var visibleNodeIds = new HashSet<string>(StringComparer.Ordinal) { "items" };
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        CrossFieldValidator.Evaluate(definition, rawValues, settledOuterValues, visibleNodeIds, errors);

        // Must agree with capture: "outer-answer" is absent there too, so this rule must not fire
        // on a value the envelope will never actually carry.
        Assert.Empty(errors);
    }

    [Fact]
    public void ARowScopedRuleFiresWhenTheOuterFieldItReadsIsStillSettledVisible()
    {
        var (definition, rows) = BuildRowScopedDefinition();

        var rawValues = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["outer-answer"] = "secret",
            ["items"] = rows,
        };
        // The exact same answer, still present after settling this time -- proving the mechanism
        // reads whatever settledOuterValues says, not that it always ignores "outer-answer".
        var settledOuterValues = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["outer-answer"] = "secret",
            ["items"] = rows,
        };
        var visibleNodeIds = new HashSet<string>(StringComparer.Ordinal) { "items" };
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        CrossFieldValidator.Evaluate(definition, rawValues, settledOuterValues, visibleNodeIds, errors);

        var key = RepeatingFieldKeys.ChildKey("confirm", rows.Rows[0].RowId);
        Assert.Equal("'Outer answer' must not be 'secret'.", errors[key]);
    }
}
