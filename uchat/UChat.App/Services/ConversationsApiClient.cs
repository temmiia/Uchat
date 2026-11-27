using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using UChat.App.Models;

namespace UChat.App.Services;

public sealed class ConversationsApiClient
{
    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url)
    {
        var request = new HttpRequestMessage(method, NetworkConfig.GetApiUri(url));

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                AuthSession.AccessToken);

        return request;
    }

    public async Task<List<ChatConversation>>
        GetConversationsAsync()
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            "api/conversations");

        using var response = await NetworkConfig.Http.SendAsync(request);

        await EnsureSuccessAsync(response);

        return await response.Content
            .ReadFromJsonAsync<List<ChatConversation>>()
            ?? new();
    }

    public async Task<ChatConversation> CreateDirectAsync(
        string username)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            "api/conversations/direct");

        request.Content = JsonContent.Create(new
        {
            TargetUsername = username
        });

        using var response = await NetworkConfig.Http.SendAsync(request);

        await EnsureSuccessAsync(response);

        return await response.Content
            .ReadFromJsonAsync<ChatConversation>()
            ?? throw new InvalidOperationException(
                "Empty API response.");
    }

    public async Task<ChatConversation> CreateGroupAsync(
        string title,
        string[] memberUsernames)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            "api/conversations/group");

        request.Content = JsonContent.Create(new
        {
            Title = title,
            MemberUsernames = memberUsernames
        });

        using var response = await NetworkConfig.Http.SendAsync(request);

        await EnsureSuccessAsync(response);

        return await response.Content
            .ReadFromJsonAsync<ChatConversation>()
            ?? throw new InvalidOperationException(
                "Empty API response.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();

        var error = $"Request failed: {(int)response.StatusCode}";

        try
        {
            using var document = JsonDocument.Parse(body);

            var root = document.RootElement;

            if (root.TryGetProperty("error", out var property))
            {
                error = property.GetString() ?? error;
            }

            if (root.TryGetProperty("usernames", out var usernames))
            {
                var missing = usernames.Deserialize<string[]>();

                if (missing is { Length: > 0 })
                    error += " " + string.Join(", ", missing);
            }
        }
        catch (JsonException)
        {
            // Keep the default HTTP error.
        }

        throw new InvalidOperationException(error);
    }
}
