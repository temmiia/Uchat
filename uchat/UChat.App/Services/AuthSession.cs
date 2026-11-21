
namespace UChat.App.Services;

public static class AuthSession
{
    public static string? AccessToken { get; private set; }

    public static bool IsAuthenticated =>
        !string.IsNullOrWhiteSpace(AccessToken);

    public static void SetToken(string token)
    {
        AccessToken = token;
    }

    public static void Clear()
    {
        AccessToken = null;
    }
}
