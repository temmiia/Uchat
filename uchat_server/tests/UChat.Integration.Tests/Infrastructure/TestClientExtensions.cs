using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using UChat.Application.Auth;

namespace UChat.Integration.Tests.Infrastructure;

public static class TestClientExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<(string AccountId, string Token)> RegisterAndLoginAsync(
        this HttpClient client,
        string username,
        string displayName,
        string password,
        CancellationToken ct = default)
    {
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            Username: username,
            DisplayName: displayName,
            Password: password
        ), ct);

        registerResponse.EnsureSuccessStatusCode();
        var regData = await registerResponse.Content.ReadFromJsonAsync<RegisterResponse>(JsonOptions, ct);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(
            Username: username,
            Password: password
        ), ct);

        loginResponse.EnsureSuccessStatusCode();
        var loginData = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, ct);

        return (regData!.AccountId, loginData!.AccessToken);
    }

    public static HttpClient CreateAuthorizedClient(
        this UChatWebApplicationFactory factory,
        string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
