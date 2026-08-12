using System.Diagnostics.CodeAnalysis;
using Microsoft.Playwright;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Shared setup for every test in this suite: a fresh <see cref="IBrowserContext"/> and
/// <see cref="Page"/> per test, from the collection-shared sample host and Chromium instance —
/// isolated cookies/storage per test without the cost of a new browser process each time.
/// </summary>
[Collection(SampleAppCollectionDefinition.Name)]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "Every concrete test class in this project is public by xUnit discovery convention, and a public class cannot derive from an internal base type (CS0060) -- this base has to be public to match. Its own members are internal, so none of them are actually part of an externally visible API.")]
public abstract class E2ETestBase : IAsyncLifetime
{
    private readonly BrowserFixture _browserFixture;
    private IBrowserContext? _context;

    private protected E2ETestBase(SampleAppFixture sampleApp, BrowserFixture browserFixture)
    {
        BaseUrl = sampleApp.BaseUrl;
        _browserFixture = browserFixture;
    }

    /// <summary>The running sample host's base URL, e.g. <c>http://127.0.0.1:53214</c>.</summary>
    internal string BaseUrl { get; }

    /// <summary>This test's own page, in its own browser context.</summary>
    internal IPage Page { get; private set; } = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _context = await _browserFixture.Browser.NewContextAsync().ConfigureAwait(false);
        Page = await _context.NewPageAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.CloseAsync().ConfigureAwait(false);
        }
    }
}
