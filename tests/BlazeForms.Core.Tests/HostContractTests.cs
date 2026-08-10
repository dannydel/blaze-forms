using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Serialization;

namespace BlazeForms.Core.Tests;

/// <summary>
/// PRD §9: hosts implement the contracts; the library ships no storage, HTTP, or auth. These
/// tests prove each contract is implementable from a host's own types alone.
/// </summary>
public sealed class HostContractTests
{
    [Fact]
    public async Task ASinkReceivesTheEnvelopeWithHiddenFieldsAbsent()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["node-trigger"] = "no",
            ["node-detail"] = "should not be submitted",
        };

        var visible = BlazeForms.Expressions.VisibilityEvaluator
            .FilterToVisible(TestDefinitions.ConditionalDefinition, values);

        var envelope = new FormSubmissionEnvelope
        {
            SubmissionId = FormIds.NewSubmissionId(),
            FormId = TestDefinitions.ConditionalDefinition.Id,
            DefinitionVersion = 1,
            StartedAt = DateTimeOffset.UnixEpoch,
            SubmittedAt = DateTimeOffset.UnixEpoch.AddMinutes(3),
            Values = FormValues.ToJsonValues(visible),
        };

        var sink = new RecordingSink();
        await ((IFormSubmissionSink)sink).SubmitAsync(envelope);

        Assert.NotNull(sink.Received);
        Assert.False(sink.Received!.Values.ContainsKey("node-detail"));
        Assert.Equal("no", sink.Received.Values["node-trigger"].GetString());
    }

    [Fact]
    public void AFieldComponentRegistryNeedsNothingButSystemTypes()
    {
        IFieldComponentRegistry registry = new PlainRegistry();

        Assert.True(registry.TryGetComponentType(NodeType.Email, out var componentType));
        Assert.Equal(typeof(PlainRegistry), componentType);
        Assert.False(registry.TryGetComponentType(NodeType.Text, out _));
    }

    [Fact]
    public void TheInMemoryImplementationsSatisfyTheirContracts()
    {
        Assert.IsAssignableFrom<IFormDefinitionStore>(new InMemoryFormDefinitionStore());
        Assert.IsAssignableFrom<IFormDraftStore>(new InMemoryFormDraftStore());
    }

    private sealed class RecordingSink : IFormSubmissionSink
    {
        public FormSubmissionEnvelope? Received { get; private set; }

        public Task SubmitAsync(FormSubmissionEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Received = envelope;
            return Task.CompletedTask;
        }
    }

    private sealed class PlainRegistry : IFieldComponentRegistry
    {
        public bool TryGetComponentType(NodeType nodeType, out Type? componentType)
        {
            componentType = nodeType == NodeType.Email ? typeof(PlainRegistry) : null;
            return componentType is not null;
        }
    }
}
