
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using UChat.App.Models;

namespace UChat.App.Services;

public sealed class MessagesApiClient
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("http://localhost:5049/")
    };

    public async Task<List<ChatMessage>> GetMessagesAsync(Guid conversationId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/conversations/{conversationId:D}/messages?limit=50");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthSession.AccessToken);

        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<List<ChatMessage>>() ?? new();
    }
}
