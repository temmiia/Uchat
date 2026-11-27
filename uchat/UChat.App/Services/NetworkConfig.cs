using System;
using System.Net.Http;

namespace UChat.App.Services;

public static class NetworkConfig
{
    private static string _baseUrl = "http://localhost:5049/";

    public static string BaseUrl
    {
        get => _baseUrl;
        set => _baseUrl = value.EndsWith('/') ? value : value + "/";
    }

    public static string HubUrl => new Uri(new Uri(BaseUrl), "hubs/chat").ToString();

    public static HttpClient Http { get; } = new HttpClient();

    public static void Initialize(string[] args)
    {
        // Support CLI args: uchat <server_ip> <port>
        if (args.Length >= 2 && int.TryParse(args[1], out var port) && port is > 0 and <= 65535)
        {
            var host = args[0];
            BaseUrl = host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? $"{host}:{port}/"
                : $"http://{host}:{port}/";
        }
        else if (args.Length == 1 && Uri.TryCreate(args[0], UriKind.Absolute, out var uri))
        {
            BaseUrl = uri.ToString();
        }
    }

    public static Uri GetApiUri(string relativePath) =>
        new(new Uri(BaseUrl), relativePath.TrimStart('/'));
}
