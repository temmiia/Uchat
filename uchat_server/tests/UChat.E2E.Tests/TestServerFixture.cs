using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace UChat.E2E.Tests;

public sealed class TestServerFixture : IAsyncDisposable
{
    private readonly string _directory;
    private readonly string _jwtKey;

    private Process? _process;

    public int Port { get; }

    public Uri BaseAddress =>
        new($"http://127.0.0.1:{Port}/");

    public string DatabasePath =>
        Path.Combine(_directory, "uchat-test.db");

    public TestServerFixture()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "UChat.E2E",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_directory);

        _jwtKey = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(64));

        Port = GetAvailablePort();
    }

    public async Task StartAsync()
    {
        if (_process is not null)
            throw new InvalidOperationException(
                "Server is already running.");

        var root = FindRepositoryRoot();

#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif

        var serverDirectory = Path.Combine(
            root,
            "uchat_server",
            "UChat.Server");

        var serverDll = Path.Combine(
            serverDirectory,
            "bin",
            configuration,
            "net10.0",
            "UChat.Server.dll");

        if (!File.Exists(serverDll))
            throw new FileNotFoundException(
                "Build the server before running E2E tests.",
                serverDll);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = serverDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(serverDll);
        startInfo.ArgumentList.Add(Port.ToString());

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] =
            "Production";

        startInfo.Environment["DOTNET_ENVIRONMENT"] =
            "Production";

        startInfo.Environment["UCHAT_JWT_KEY"] =
            _jwtKey;

        startInfo.Environment["UCHAT_JWT_ISSUER"] =
            "UChat.E2E.Server";

        startInfo.Environment["UCHAT_JWT_AUDIENCE"] =
            "UChat.E2E.Client";

        startInfo.Environment["ConnectionStrings__UChat"] =
            $"Data Source={DatabasePath};Pooling=False";

        startInfo.Environment["EnableHttpsRedirection"] =
            "false";

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Could not start UChat server.");

        using var http = new HttpClient
        {
            BaseAddress = BaseAddress,
            Timeout = TimeSpan.FromSeconds(2)
        };

        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Server exited with code {_process.ExitCode}.");
            }

            try
            {
                using var response =
                    await http.GetAsync("health");

                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // Server is still starting.
            }
            catch (TaskCanceledException)
            {
                // Health request timed out.
            }

            await Task.Delay(200);
        }

        throw new TimeoutException(
            "UChat server did not become healthy.");
    }

    public async Task StopAsync()
    {
        var process = _process;

        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);

                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15));
            }
        }
        finally
        {
            process.Dispose();
            _process = null;
        }
    }

    public async Task RestartAsync()
    {
        await StopAsync();
        await StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(
                _directory,
                recursive: true);
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(
            IPAddress.Loopback, 0);

        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint)
                .Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                directory.FullName, "uchat.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate uchat.slnx.");
    }
}