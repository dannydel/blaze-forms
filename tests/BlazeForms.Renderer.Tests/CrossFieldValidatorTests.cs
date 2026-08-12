using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Internal;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="CrossFieldValidator"/> in isolation: which rules fire, which are skipped,
/// and how a target that already carries a per-field error is left alone (PRD §6).
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

        CrossFieldValidator.Evaluate(definition, values, visibleNodeIds, errors);

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

        CrossFieldValidator.Evaluate(definition, values, visibleNodeIds, errors);

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

        CrossFieldValidator.Evaluate(definition, values, visibleNodeIds, errors);

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

        CrossFieldValidator.Evaluate(definition, values, visibleNodeIds, errors);

        Assert.Equal("Enter a value for 'Employer'.", errors["employer"]);
    }
}
