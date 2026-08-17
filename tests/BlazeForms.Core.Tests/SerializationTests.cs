using System.Text.Json;
using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Hosting;
using BlazeForms.Serialization;

namespace BlazeForms.Core.Tests;

/// <summary>
/// The definition JSON is a public contract (AGENTS.md invariant #2), so the wire names of
/// every node type, operator, and join are pinned here as well as in the golden file.
/// </summary>
public sealed class SerializationTests
{
    [Fact]
    public void ADefinitionRoundTripsWithoutLoss()
    {
        var json = FormJson.SerializeDefinition(TestDefinitions.RepresentativeDefinition);
        var restored = FormJson.DeserializeDefinition(json);

        Assert.Equal(json, FormJson.SerializeDefinition(restored));
    }

    [Fact]
    public void RoundTrippingPreservesStructureAndLogic()
    {
        var restored = FormJson.DeserializeDefinition(
            FormJson.SerializeDefinition(TestDefinitions.RepresentativeDefinition));

        Assert.Equal("form-transport-enrollment", restored.Id);
        Assert.Equal(2, restored.Pages.Count);
        Assert.Equal(13, restored.Pages[0].Sections[0].Nodes.Count);

        var busRoute = restored.FindNode("node-bus-route");
        Assert.NotNull(busRoute);
        Assert.Equal(NodeType.Select, busRoute!.Type);
        Assert.True(busRoute.RequiredWhenVisible);
        Assert.Equal(2, busRoute.Options.Count);
        Assert.Equal("route-a", busRoute.Options[0].Value);
        Assert.Equal(ConditionJoin.All, busRoute.VisibleWhen!.Join);
        Assert.Equal(ConditionOperator.Is, busRoute.VisibleWhen.Conditions[0].Operator);
        Assert.Equal("node-needs-transport", busRoute.VisibleWhen.Conditions[0].Field);

        var gradeLevel = restored.FindNode("node-grade-level");
        Assert.Equal(0m, gradeLevel!.Min);
        Assert.Equal(12m, gradeLevel.Max);

        var siblings = restored.FindNode("node-siblings");
        Assert.Single(siblings!.Children);

        Assert.Single(restored.ValidationRules);
        Assert.Equal("node-bus-route", restored.ValidationRules[0].Target);
        Assert.Equal(2, restored.ValidationRules[0].Expression.Conditions.Count);
    }

    [Fact]
    public void SchemaVersionIsWrittenAndDefaultsToTheCurrentVersion()
    {
        Assert.Equal(3, FormSchema.CurrentVersion);
        Assert.Equal(FormSchema.CurrentVersion, TestDefinitions.RepresentativeDefinition.SchemaVersion);

        var json = FormJson.SerializeDefinition(TestDefinitions.RepresentativeDefinition);

        Assert.StartsWith("{\"schemaVersion\":3,", json, StringComparison.Ordinal);
    }

    [Fact]
    public void OmittedSchemaVersionDeserializesToTheCurrentVersion()
    {
        var restored = FormJson.DeserializeDefinition("""{"id":"f","name":"F"}""");

        Assert.Equal(FormSchema.CurrentVersion, restored.SchemaVersion);
    }

    [Fact]
    public void ASchemaVersionThisBuildCannotReadIsRejected()
    {
        Assert.Throws<JsonException>(() =>
            FormJson.DeserializeDefinition("""{"schemaVersion":0,"id":"f","name":"F"}"""));
        Assert.Throws<JsonException>(() =>
            FormJson.DeserializeDefinition("""{"schemaVersion":-1,"id":"f","name":"F"}"""));
        Assert.Throws<JsonException>(() =>
            FormJson.DeserializeDefinition($$"""{"schemaVersion":{{FormSchema.CurrentVersion + 1}},"id":"f","name":"F"}"""));
        Assert.Throws<JsonException>(() =>
            FormJson.DeserializeDefinition("""{"schemaVersion":"1","id":"f","name":"F"}"""));
    }

    [Fact]
    public void EverySchemaVersionThisBuildKnowsIsAccepted()
    {
        for (var version = 1; version <= FormSchema.CurrentVersion; version++)
        {
            var restored = FormJson.DeserializeDefinition($$"""{"schemaVersion":{{version}},"id":"f","name":"F"}""");

            Assert.Equal(version, restored.SchemaVersion);
        }
    }

    [Fact]
    public void ASparseDocumentDeserializesWithEmptyCollectionsRatherThanNulls()
    {
        var restored = FormJson.DeserializeDefinition(
            """{"id":"f","name":"F","pages":[{"id":"p","sections":[{"id":"s","nodes":[{"id":"n","type":"select","visibleWhen":{}}]}]}]}""");

        Assert.Empty(restored.ValidationRules);
        Assert.Empty(restored.EnumerateNodes().Single().Options);
        Assert.Empty(restored.EnumerateNodes().Single().Children);
        Assert.Empty(restored.EnumerateNodes().Single().VisibleWhen!.Conditions);

        var sparsePages = FormJson.DeserializeDefinition("""{"id":"f","name":"F"}""");
        Assert.Empty(sparsePages.Pages);
        Assert.Empty(sparsePages.EnumerateNodes());
    }

    [Fact]
    public void ASparseEnvelopeDeserializesWithNoAnswersRatherThanNull()
    {
        var restored = FormJson.DeserializeEnvelope(
            """{"submissionId":"s","formId":"f","definitionVersion":1,"startedAt":"2026-08-10T09:00:00+00:00","submittedAt":"2026-08-10T09:01:00+00:00"}""");

        Assert.Empty(restored.Values);
        Assert.Null(restored.RespondentKey);
    }

    [Fact]
    public void NullPropertiesAreOmittedFromTheWireFormat()
    {
        var definition = new FormDefinition { Id = "f", Name = "F" };

        var json = FormJson.SerializeDefinition(definition);

        Assert.DoesNotContain("\"description\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("null", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NodeType.Text, "text")]
    [InlineData(NodeType.TextArea, "textarea")]
    [InlineData(NodeType.Email, "email")]
    [InlineData(NodeType.Phone, "phone")]
    [InlineData(NodeType.Number, "number")]
    [InlineData(NodeType.Currency, "currency")]
    [InlineData(NodeType.Date, "date")]
    [InlineData(NodeType.DateRange, "daterange")]
    [InlineData(NodeType.Select, "select")]
    [InlineData(NodeType.Radio, "radio")]
    [InlineData(NodeType.CheckboxGroup, "checkboxgroup")]
    [InlineData(NodeType.YesNo, "yesno")]
    [InlineData(NodeType.Boolean, "boolean")]
    [InlineData(NodeType.Heading, "heading")]
    [InlineData(NodeType.Paragraph, "paragraph")]
    [InlineData(NodeType.Callout, "callout")]
    [InlineData(NodeType.Divider, "divider")]
    [InlineData(NodeType.Calc, "calc")]
    [InlineData(NodeType.Repeating, "repeating")]
    [InlineData(NodeType.File, "file")]
    [InlineData(NodeType.Lookup, "lookup")]
    public void EveryNodeTypeSerializesToItsDocumentedName(NodeType nodeType, string expected)
    {
        var definition = new FormDefinition
        {
            Id = "f",
            Name = "F",
            Pages =
            [
                new FormPage
                {
                    Id = "p",
                    Sections = [new FormSection { Id = "s", Nodes = [new FormNode { Id = "n", Type = nodeType }] }],
                },
            ],
        };

        var json = FormJson.SerializeDefinition(definition);

        Assert.Contains($"\"type\":\"{expected}\"", json, StringComparison.Ordinal);
        Assert.Equal(nodeType, FormJson.DeserializeDefinition(json).FindNode("n")!.Type);
    }

    /// <summary>
    /// <see cref="NodeType.Repeating"/> was un-reserved in the repeating-groups Designer slice
    /// (repeating-groups-plan.md, Increment C): the Core answer model, row-scoped evaluation, and
    /// fillable Renderer group all shipped in Increments A and B, so it now joins
    /// <see cref="FormSchema.PhaseOneNodeTypes"/> alongside the original 18, leaving only File and
    /// Lookup reserved for a later phase.
    /// </summary>
    [Fact]
    public void TheSchemaCoversEveryAddableNodeTypeAndReservesTheRemainingTwo()
    {
        Assert.Equal(19, FormSchema.PhaseOneNodeTypes.Count);
        Assert.Equal(2, FormSchema.ReservedNodeTypes.Count);
        Assert.Equal(
            Enum.GetValues<NodeType>().Length,
            FormSchema.PhaseOneNodeTypes.Count + FormSchema.ReservedNodeTypes.Count);
        Assert.Contains(NodeType.Repeating, FormSchema.PhaseOneNodeTypes);
        Assert.Contains(NodeType.File, FormSchema.ReservedNodeTypes);
        Assert.Contains(NodeType.Lookup, FormSchema.ReservedNodeTypes);
        Assert.DoesNotContain(NodeType.Divider, FormSchema.ReservedNodeTypes);
    }

    [Fact]
    public void StaticNodesCarryNoAnswerAndInputNodesDo()
    {
        Assert.False(FormSchema.IsInputNode(NodeType.Heading));
        Assert.False(FormSchema.IsInputNode(NodeType.Paragraph));
        Assert.False(FormSchema.IsInputNode(NodeType.Callout));
        Assert.False(FormSchema.IsInputNode(NodeType.Divider));
        Assert.True(FormSchema.IsInputNode(NodeType.Text));
        Assert.True(FormSchema.IsInputNode(NodeType.Calc));
    }

    [Theory]
    [InlineData(ConditionOperator.Is, "is")]
    [InlineData(ConditionOperator.IsNot, "isNot")]
    [InlineData(ConditionOperator.IsTrue, "isTrue")]
    [InlineData(ConditionOperator.IsFalse, "isFalse")]
    [InlineData(ConditionOperator.IsBlank, "isBlank")]
    [InlineData(ConditionOperator.IsNotBlank, "isNotBlank")]
    [InlineData(ConditionOperator.GreaterThan, "gt")]
    [InlineData(ConditionOperator.LessThan, "lt")]
    [InlineData(ConditionOperator.Contains, "contains")]
    public void EveryOperatorSerializesToItsDocumentedName(ConditionOperator conditionOperator, string expected)
    {
        var json = FormJson.SerializeConditionGroup(new ConditionGroup
        {
            Conditions = [new Condition { Field = "x", Operator = conditionOperator, Value = "v" }],
        });

        Assert.Contains($"\"op\":\"{expected}\"", json, StringComparison.Ordinal);
        Assert.Equal(conditionOperator, FormJson.DeserializeConditionGroup(json).Conditions[0].Operator);
    }

    [Theory]
    [InlineData(ConditionJoin.All, "all")]
    [InlineData(ConditionJoin.Any, "any")]
    public void EveryJoinSerializesToItsDocumentedName(ConditionJoin join, string expected)
    {
        var json = FormJson.SerializeConditionGroup(new ConditionGroup { Join = join });

        Assert.Contains($"\"join\":\"{expected}\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CalcOperation.Sum, "sum")]
    [InlineData(CalcOperation.Subtract, "subtract")]
    [InlineData(CalcOperation.Multiply, "multiply")]
    [InlineData(CalcOperation.Divide, "divide")]
    [InlineData(CalcOperation.DateAddDays, "dateAddDays")]
    [InlineData(CalcOperation.DateDiffDays, "dateDiffDays")]
    public void EveryCalcOperationSerializesToItsDocumentedName(CalcOperation operation, string expected)
    {
        var json = FormJson.SerializeCalcExpression(new CalcExpression { Operation = operation });

        Assert.Contains($"\"op\":\"{expected}\"", json, StringComparison.Ordinal);
        Assert.Equal(operation, FormJson.DeserializeCalcExpression(json).Operation);
    }

    [Theory]
    [InlineData(CalcFormat.Number, "number")]
    [InlineData(CalcFormat.Integer, "integer")]
    [InlineData(CalcFormat.Currency, "currency")]
    [InlineData(CalcFormat.Date, "date")]
    public void EveryCalcFormatSerializesToItsDocumentedName(CalcFormat format, string expected)
    {
        var json = FormJson.SerializeCalcExpression(new CalcExpression { Operation = CalcOperation.Sum, Format = format });

        Assert.Contains($"\"format\":\"{expected}\"", json, StringComparison.Ordinal);
        Assert.Equal(format, FormJson.DeserializeCalcExpression(json).Format);
    }

    [Fact]
    public void TheCalcFunctionSerializesToItsDocumentedName()
    {
        var json = FormJson.SerializeCalcExpression(new CalcExpression
        {
            Operation = CalcOperation.Sum,
            Operands = [new CalcOperand { Function = CalcFunction.Today }],
        });

        Assert.Contains("\"function\":\"today\"", json, StringComparison.Ordinal);
        Assert.Equal(CalcFunction.Today, FormJson.DeserializeCalcExpression(json).Operands[0].Function);
    }

    [Fact]
    public void ACalcOperandSerializesOnlyItsSetMember()
    {
        var expression = new CalcExpression
        {
            Operation = CalcOperation.Sum,
            Operands =
            [
                new CalcOperand { Field = "node-fee" },
                new CalcOperand { Number = 50m },
                new CalcOperand { Function = CalcFunction.Today },
            ],
        };

        var json = FormJson.SerializeCalcExpression(expression);

        Assert.Equal("""{"op":"sum","operands":[{"field":"node-fee"},{"number":50},{"function":"today"}],"format":"number"}""", json);

        var restored = FormJson.DeserializeCalcExpression(json);
        Assert.Equal("node-fee", restored.Operands[0].Field);
        Assert.Null(restored.Operands[0].Number);
        Assert.Equal(50m, restored.Operands[1].Number);
        Assert.Equal(CalcFunction.Today, restored.Operands[2].Function);
    }

    [Fact]
    public void ACalcExpressionRoundTripsWithinADefinition()
    {
        var restored = FormJson.DeserializeDefinition(
            FormJson.SerializeDefinition(TestDefinitions.RepresentativeDefinition));

        var estimatedCost = restored.FindNode("node-estimated-cost");
        Assert.NotNull(estimatedCost);
        Assert.Equal(NodeType.Calc, estimatedCost!.Type);
        Assert.NotNull(estimatedCost.Calculation);
        Assert.Equal(CalcOperation.Sum, estimatedCost.Calculation!.Operation);
        Assert.Equal(CalcFormat.Currency, estimatedCost.Calculation.Format);
        Assert.Equal(2, estimatedCost.Calculation.Operands.Count);
        Assert.Equal("node-annual-fee", estimatedCost.Calculation.Operands[0].Field);
        Assert.Equal(50m, estimatedCost.Calculation.Operands[1].Number);
    }

    [Fact]
    public void TheSubmissionEnvelopeRoundTrips()
    {
        var answers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-first-name"] = "Ada",
            ["node-grade-level"] = 9m,
            ["node-consent"] = true,
            ["node-date-of-birth"] = new DateOnly(2016, 4, 2),
            ["node-accommodations"] = new[] { "lift", "aide" },
        };

        var envelope = new FormSubmissionEnvelope
        {
            SubmissionId = "sub-1",
            FormId = "form-transport-enrollment",
            DefinitionVersion = 3,
            StartedAt = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero),
            SubmittedAt = new DateTimeOffset(2026, 8, 10, 9, 12, 30, TimeSpan.Zero),
            RespondentKey = "opaque-respondent-key",
            Values = FormValues.ToJsonValues(answers),
        };

        var json = FormJson.SerializeEnvelope(envelope);
        var restored = FormJson.DeserializeEnvelope(json);

        Assert.Equal("sub-1", restored.SubmissionId);
        Assert.Equal(3, restored.DefinitionVersion);
        Assert.Equal(envelope.SubmittedAt, restored.SubmittedAt);
        Assert.Equal("opaque-respondent-key", restored.RespondentKey);
        Assert.Equal(5, restored.Values.Count);
        Assert.Equal("Ada", restored.Values["node-first-name"].GetString());
        Assert.Equal(9m, restored.Values["node-grade-level"].GetDecimal());
        Assert.True(restored.Values["node-consent"].GetBoolean());
        Assert.Equal(JsonValueKind.Array, restored.Values["node-accommodations"].ValueKind);
    }

    [Fact]
    public void DeserializingALiteralNullIsAnError()
    {
        Assert.Throws<JsonException>(() => FormJson.DeserializeDefinition("null"));
        Assert.Throws<JsonException>(() => FormJson.DeserializeEnvelope("null"));
    }

    [Fact]
    public void SerializationHelpersRejectNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => FormJson.SerializeDefinition(null!));
        Assert.Throws<ArgumentNullException>(() => FormJson.DeserializeDefinition(null!));
        Assert.Throws<ArgumentNullException>(() => FormJson.SerializeEnvelope(null!));
    }

    [Fact]
    public void TheTypeInfoResolverIsAvailableForHostJsonOptions()
    {
        Assert.NotNull(FormJson.TypeInfoResolver);
        Assert.NotNull(FormJson.Options.TypeInfoResolver);
    }
}
