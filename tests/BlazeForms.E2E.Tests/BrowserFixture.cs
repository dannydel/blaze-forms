using System.Diagnostics.CodeAnalysis;
using Microsoft.Playwright;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Owns one headless Chromium instance for the whole test run. Launching a browser process is
/// expensive enough that every scenario in this suite shares it, opening a fresh
/// <see cref="IBrowserContext"/> per test instead — that gets each test its own cookies, storage,
/// and viewport without paying for a new browser process (PRD §11).
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "Every concrete test class takes this as a public constructor parameter (xUnit instantiates test classes via a public constructor), and a public constructor cannot have an internally-typed parameter (CS0051) -- this has to be public to match.")]
public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    /// <summary>
    /// The shared Chromium instance. Only meaningful once <see cref="InitializeAsync"/> has
    /// completed.
    /// </summary>
    public IBrowser Browser { get; private set; } = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await Browser.CloseAsync().ConfigureAwait(false);
        _playwright?.Dispose();
    }
}
