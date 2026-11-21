
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using UChat.App.Models;

namespace UChat.App.Services;

public sealed class ConversationsApiClient
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("http://localhost:5049/")
    };

    private static void SetAuthorization()
    {
        Http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthSession.AccessToken);
    }

    public async Task<List<ChatConversation>> GetConversationsAsync()
    {
        SetAuthorization();

        return await Http.GetFromJsonAsync<List<ChatConversation>>(
            "api/conversations") ?? new();
    }

    public async Task<ChatConversation> CreateDirectAsync(string username)
    {
        SetAuthorization();

        using var response = await Http.PostAsJsonAsync(
            "api/conversations/direct",
            new { TargetUsername = username });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ChatConversation>()
            ?? throw new InvalidOperationException("Empty API response.");
    }
}
