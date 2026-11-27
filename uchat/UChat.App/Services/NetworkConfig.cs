using System;
using System.Net.Http;

namespace UChat.App.Services;

public static class NetworkConfig
{
    private static string _baseUrl = Environment.GetEnvironmentVariable("UCHAT_SERVER_URL")
        ?? Environment.GetEnvironmentVariable("UCHAT_API_URL")
        ?? "http://localhost:5049/";

    public static string BaseUrl
    {
        get => _baseUrl;
        set => _baseUrl = value.EndsWith('/') ? value : value + "/";
    }

    public static string HubUrl => new Uri(new Uri(BaseUrl), "hubs/chat").ToString();

    public static HttpClient Http { get; } = new HttpClient();

    public static void Initialize(string[] args)
    {
        var envUrl = Environment.GetEnvironmentVariable("UCHAT_SERVER_URL")
            ?? Environment.GetEnvironmentVariable("UCHAT_API_URL");

        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            BaseUrl = envUrl;
        }

        // Support CLI args: uchat <server_ip_or_url> [port]
        if (args.Length >= 2 && int.TryParse(args[1], out var port) && port is > 0 and <= 65535)
        {
            var host = args[0];
            BaseUrl = host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? $"{host}:{port}/"
                : $"http://{host}:{port}/";
        }
        else if (args.Length == 1)
        {
            var target = args[0];
            if (int.TryParse(target, out var p) && p is > 0 and <= 65535)
            {
                BaseUrl = $"http://localhost:{p}/";
            }
            else if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
            {
                BaseUrl = uri.ToString();
            }
            else
            {
                BaseUrl = $"http://{target}:5049/";
            }
        }
    }

    public static Uri GetApiUri(string relativePath) =>
        new(new Uri(BaseUrl), relativePath.TrimStart('/'));
}
