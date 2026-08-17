using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Fields;
using BlazeForms.Hosting;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers the internal resolver in <c>Fields/DefaultFieldComponents.cs</c>: registry-first
/// lookup, the shipped default fallback, and its two failure modes.
/// </summary>
public sealed class DefaultFieldComponentsTests
{
    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Used only as a Type identity passed to the resolver under test; the resolver never constructs it.")]
    private sealed class FakeFieldComponent : FormFieldBase
    {
    }

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Used only as a Type identity passed to the resolver under test; the resolver never constructs it.")]
    private sealed class NotAFieldComponent : ComponentBase
    {
    }

    private sealed class StubRegistry : IFieldComponentRegistry
    {
        private readonly Dictionary<NodeType, Type> _registrations = [];

        public void Register(NodeType nodeType, Type componentType) => _registrations[nodeType] = componentType;

        public bool TryGetComponentType(NodeType nodeType, out Type? componentType) =>
            _registrations.TryGetValue(nodeType, out componentType);
    }

    [Fact]
    public void FallsBackToTheShippedDefaultWhenNoRegistryIsSupplied()
    {
        var resolved = DefaultFieldComponents.Resolve(NodeType.Text, registry: null);

        Assert.Equal(typeof(TextField), resolved);
    }

    [Fact]
    public void FallsBackToTheShippedDefaultWhenTheRegistryHasNoOverride()
    {
        var registry = new StubRegistry();

        var resolved = DefaultFieldComponents.Resolve(NodeType.Email, registry);

        Assert.Equal(typeof(EmailField), resolved);
    }

    [Fact]
    public void HonorsARegistryOverrideThatDerivesFromFormFieldBase()
    {
        var registry = new StubRegistry();
        registry.Register(NodeType.Text, typeof(FakeFieldComponent));

        var resolved = DefaultFieldComponents.Resolve(NodeType.Text, registry);

        Assert.Equal(typeof(FakeFieldComponent), resolved);
    }

    [Fact]
    public void ThrowsWhenTheRegistryOverrideDoesNotDeriveFromFormFieldBase()
    {
        var registry = new StubRegistry();
        registry.Register(NodeType.Text, typeof(NotAFieldComponent));

        Assert.Throws<InvalidOperationException>(() => DefaultFieldComponents.Resolve(NodeType.Text, registry));
    }

    [Fact]
    public void ThrowsForAStillReservedNodeTypeWithNoRegistryOverride()
    {
        // File and Lookup still ship schema only in this slice -- no default renderer, and no
        // structural branch anywhere in FormRenderer resolves them.
        Assert.Throws<InvalidOperationException>(() => DefaultFieldComponents.Resolve(NodeType.File, registry: null));
        Assert.Throws<InvalidOperationException>(() => DefaultFieldComponents.Resolve(NodeType.Lookup, registry: null));
    }

    /// <summary>
    /// <see cref="NodeType.Repeating"/> is no longer reserved (repeating-groups-plan.md's schema
    /// v3): a fillable group exists, but it is resolved structurally by <c>FormRenderer</c>'s
    /// section loop rendering the internal <c>Components.RepeatingGroup</c> directly, never
    /// through this resolver. A direct call here for <see cref="NodeType.Repeating"/> with no
    /// registry override — the one path that reaches this type instead of that structural
    /// branch — documents that by still throwing, exactly like the two node types this build
    /// fully reserves.
    /// </summary>
    [Fact]
    public void ThrowsForRepeatingWithNoRegistryOverrideSinceItIsResolvedStructurallyNotHere()
    {
        Assert.Throws<InvalidOperationException>(() => DefaultFieldComponents.Resolve(NodeType.Repeating, registry: null));
    }

    /// <summary>
    /// A host registering its own component for <see cref="NodeType.Repeating"/> is the one case
    /// this resolver ever answers for it — <c>FormRenderer</c>'s section loop checks the registry
    /// first and falls through to this resolver's ordinary registry-first path only then.
    /// </summary>
    [Fact]
    public void HonorsARegistryOverrideForRepeating()
    {
        var registry = new StubRegistry();
        registry.Register(NodeType.Repeating, typeof(FakeFieldComponent));

        var resolved = DefaultFieldComponents.Resolve(NodeType.Repeating, registry);

        Assert.Equal(typeof(FakeFieldComponent), resolved);
    }

    [Fact]
    public void EveryPhaseOneNodeTypeResolvesToADefaultFormFieldBaseSubclass()
    {
        foreach (var nodeType in FormSchema.PhaseOneNodeTypes)
        {
            var resolved = DefaultFieldComponents.Resolve(nodeType, registry: null);
            Assert.True(typeof(FormFieldBase).IsAssignableFrom(resolved), $"{nodeType} resolved to {resolved}.");
        }
    }
}
