using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BlazeForms.E2E.Tests;

/// <summary>
/// Launches <c>samples/BlazeForms.Sample</c> as a real background process, listening on a free
/// loopback port, and tears it back down again. Every scenario in this suite needs an actual
/// Interactive Server circuit over a real socket — <c>WebApplicationFactory</c> cannot host one —
/// so this fixture drives <c>dotnet run</c> directly rather than an in-process test host (PRD
/// §4.2, §11; AGENTS.md invariant #4). One instance is shared by every test through
/// <see cref="SampleAppCollectionDefinition"/>, so the process starts and stops exactly once per
/// test run.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "Every concrete test class takes this as a public constructor parameter (xUnit instantiates test classes via a public constructor), and a public constructor cannot have an internally-typed parameter (CS0051) -- this has to be public to match.")]
public sealed class SampleAppFixture : IAsyncLifetime, IDisposable
{
    private const int StartupTimeoutSeconds = 90;

    private Process? _process;
    private readonly StringBuilder _output = new();

    /// <summary>
    /// The sample host's base URL, e.g. <c>http://127.0.0.1:53214</c>. Only meaningful once
    /// <see cref="InitializeAsync"/> has completed.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1056:URI-like properties should not be strings",
        Justification = "Every caller hands this straight to Playwright APIs (IPage.GotoAsync, string interpolation building a full URL) that want a string, not a System.Uri -- a Uri here would just get ToString()'d back out at every call site.")]
    public string BaseUrl { get; private set; } = "";

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var port = GetFreeTcpPort();
        BaseUrl = $"http://127.0.0.1:{port}";

        var repoRoot = FindRepoRoot();
        var sampleProjectPath = Path.Combine(repoRoot, "samples", "BlazeForms.Sample", "BlazeForms.Sample.csproj");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(sampleProjectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(BaseUrl);
        // Development, deliberately: ASP.NET Core only auto-wires a referenced project's static
        // web assets (BlazeForms.Renderer's blazeforms.css, the Blazor Server framework scripts,
        // MudBlazor's own assets) under `dotnet run` when the environment is Development --
        // that wiring assumes a real `dotnet publish` output otherwise, which this fixture never
        // produces. Skipping launchSettings.json (--no-launch-profile, just above) would default
        // to Production and 404 every one of those assets, so it is set explicitly here instead.
        startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += OnOutputReceived;
        _process.ErrorDataReceived += OnOutputReceived;

        if (!_process.Start())
        {
            throw new InvalidOperationException("Failed to start the sample host process.");
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilServingAsync().ConfigureAwait(false);
    }

    private void OnOutputReceived(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
        {
            return;
        }

        lock (_output)
        {
            _output.AppendLine(e.Data);
        }
    }

    /// <summary>
    /// Polls the sample host's root page until it answers with a successful status, the process
    /// exits early, or <see cref="StartupTimeoutSeconds"/> elapses — whichever comes first. `dotnet
    /// run` builds the sample (and, on a cold run, its <c>BlazeForms.Core</c>/<c>.Renderer</c>/
    /// <c>.Designer</c> project references) before it starts listening, so the timeout is generous
    /// rather than tuned to an already-warm build.
    /// </summary>
    private async Task WaitUntilServingAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var rootUri = new Uri(BaseUrl);
        var deadline = DateTime.UtcNow.AddSeconds(StartupTimeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (_process!.HasExited)
            {
                throw new InvalidOperationException(
                    $"The sample host exited early (code {_process.ExitCode}) before it started serving requests. Output:{Environment.NewLine}{_output}");
            }

            try
            {
                using var response = await client.GetAsync(rootUri).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Nothing listening on the port yet -- keep polling.
            }
            catch (TaskCanceledException)
            {
                // A single poll attempt timed out -- keep polling within the overall deadline.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"The sample host never started serving requests within {StartupTimeoutSeconds}s. Output:{Environment.NewLine}{_output}");
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_process is { HasExited: false } process)
        {
            try
            {
                // `dotnet run` starts the compiled app as a child process on every platform this
                // suite targets -- killing only the `dotnet run` process itself would leave the
                // actual Kestrel host (and its bound port) running.
                process.Kill(entireProcessTree: true);

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Exited between the HasExited check and the kill -- nothing left to clean up.
            }
            catch (OperationCanceledException)
            {
                // Killed but slow to tear down -- Dispose() below still runs.
            }
        }

        Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _process?.Dispose();
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Walks upward from the test assembly's output directory to find the repository root,
    /// identified by <c>BlazeForms.sln</c> -- the same root <c>samples/BlazeForms.Sample</c>
    /// resolves from, regardless of whether this project runs from a local build or a CI runner's
    /// checkout path.
    /// </summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BlazeForms.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Could not locate BlazeForms.sln above {AppContext.BaseDirectory}.");
    }
}
