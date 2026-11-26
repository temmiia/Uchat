using System;
using System.Text.Json;

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

            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');

            var bytes = Convert.FromBase64String(payload);

            using var document = JsonDocument.Parse(bytes);

            if (document.RootElement.TryGetProperty("sub", out var sub))
                return sub.GetString();

            if (document.RootElement.TryGetProperty(
                "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
                out var nameId))
            {
                return nameId.GetString();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
