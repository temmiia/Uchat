
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace UChat.App.Services;

public sealed class AuthApiClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task RegisterAsync(
        string username,
        string displayName,
        string password)
    {
        using var response = await NetworkConfig.Http.PostAsJsonAsync(
            NetworkConfig.GetApiUri("api/auth/register"),
            new { Username = username, DisplayName = displayName, Password = password });

        await EnsureSuccessAsync(response);
    }

    public async Task<string> LoginAsync(string username, string password)
    {
        using var response = await NetworkConfig.Http.PostAsJsonAsync(
            NetworkConfig.GetApiUri("api/auth/login"),
            new { Username = username, Password = password });

        await EnsureSuccessAsync(response);

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(
            JsonOptions);

        if (string.IsNullOrWhiteSpace(result?.AccessToken))
            throw new InvalidOperationException("Server did not return an access token.");

        return result.AccessToken;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        string message = $"Request failed ({(int)response.StatusCode}).";

        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(
                JsonOptions);

            if (!string.IsNullOrWhiteSpace(error?.Error))
                message = error.Error;
        }
        catch (JsonException)
        {
            // Keep the HTTP status error if the response is not JSON.
        }

        throw new HttpRequestException(message);
    }

    private sealed record LoginResponse(string AccessToken, string TokenType, int ExpiresIn);
    private sealed record ApiError(string Error);
}
