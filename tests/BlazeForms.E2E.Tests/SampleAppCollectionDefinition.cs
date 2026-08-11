using System.Diagnostics.CodeAnalysis;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Groups every test in this suite onto one shared <see cref="SampleAppFixture"/> and
/// <see cref="BrowserFixture"/> — one sample host process and one Chromium instance for the whole
/// run, rather than one of each per test class.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit's own analyzer (xUnit1027) requires a collection definition class to be public; nothing outside this assembly references it, but it cannot be internal.")]
[CollectionDefinition(Name)]
public sealed class SampleAppCollectionDefinition : ICollectionFixture<SampleAppFixture>, ICollectionFixture<BrowserFixture>
{
    /// <summary>
    /// The collection name every test class in this suite passes to <c>[Collection(...)]</c>.
    /// </summary>
    public const string Name = "Sample app";
}
