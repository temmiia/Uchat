
using System;

namespace UChat.App.Services;

public static class AuthSession
{
    public static string? AccessToken { get; private set; }
    public static string? AccountId { get; private set; }

    public static bool IsAuthenticated =>
        !string.IsNullOrWhiteSpace(AccessToken);

    public static void SetToken(string token)
    {
        AccessToken = token;
        AccountId = ReadAccountId(token);
    }

    public static void Clear()
    {
        AccessToken = null;
        AccountId = null;
    }

    private static string? ReadAccountId(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
                return null;

            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');

            using var document = System.Text.Json.JsonDocument.Parse(
                Convert.FromBase64String(payload));

            return document.RootElement.TryGetProperty("sub", out var sub)
                ? sub.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
