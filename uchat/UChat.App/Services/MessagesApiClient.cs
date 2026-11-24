using System;
using System.Collections.Generic;
using System.Globalization;
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

    public async Task<List<ChatMessage>> GetMessagesAsync(
        Guid conversationId,
        int limit = 50,
        DateTime? beforeUtc = null)
    {
        var url = $"api/conversations/{conversationId:D}/messages?limit={limit}";

        if (beforeUtc.HasValue)
        {
            var before = beforeUtc.Value
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture);

            url += $"&before={Uri.EscapeDataString(before)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer", AuthSession.AccessToken);

        using var response = await Http.SendAsync(request);

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<List<ChatMessage>>() ?? new();
    }

    public async Task EditMessageAsync(
        Guid conversationId,
        Guid messageId,
        string content)
    {
        var url =
            $"api/conversations/{conversationId:D}/messages/{messageId:D}";

        using var request = new HttpRequestMessage(HttpMethod.Patch, url);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer", AuthSession.AccessToken);

        request.Content = JsonContent.Create(new { Content = content });

        using var response = await Http.SendAsync(request);

        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteMessageAsync(
        Guid conversationId,
        Guid messageId)
    {
        var url =
            $"api/conversations/{conversationId:D}/messages/{messageId:D}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer", AuthSession.AccessToken);

        using var response = await Http.SendAsync(request);

        response.EnsureSuccessStatusCode();
    }
}
