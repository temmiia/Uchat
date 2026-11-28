using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using UChat.Application.Messages;

namespace UChat.E2E.Tests;

public sealed class TestChatClient : IAsyncDisposable
{
    private const string Password = "UChat_Test_2026!";

    private readonly Uri _baseAddress;

    public HttpClient Http { get; }

    public HubConnection Hub { get; private set; } = null!;

    public string Username { get; }

    public string DisplayName { get; }

    public string AccessToken { get; private set; } = "";

    public TestChatClient(
        Uri baseAddress,
        string username,
        string displayName)
    {
        _baseAddress = baseAddress;
        Username = username;
        DisplayName = displayName;

        Http = new HttpClient
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task RegisterAsync()
    {
        using var response = await Http.PostAsJsonAsync(
            "api/auth/register",
            new
            {
                Username,
                DisplayName,
                Password
            });

        await EnsureStatusAsync(
            response,
            HttpStatusCode.Created);
    }

    public async Task LoginAsync()
    {
        using var response = await Http.PostAsJsonAsync(
            "api/auth/login",
            new
            {
                Username,
                Password
            });

        await EnsureStatusAsync(
            response,
            HttpStatusCode.OK);

        var login = await response.Content
            .ReadFromJsonAsync<LoginResult>();

        Assert.NotNull(login);
        Assert.False(
            string.IsNullOrWhiteSpace(login.AccessToken));

        AccessToken = login.AccessToken;

        Http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                AccessToken);
    }

    public async Task ConnectAsync()
    {
        if (Hub is not null)
            await Hub.DisposeAsync();

        Hub = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_baseAddress, "hubs/chat"),
                options =>
                {
                    options.AccessTokenProvider =
                        () => Task.FromResult<string?>(
                            AccessToken);
                })
            .Build();

        using var timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(15));

        await Hub.StartAsync(timeout.Token);
    }

    public async Task DisconnectAsync()
    {
        if (Hub is not null)
            await Hub.StopAsync()
                .WaitAsync(TimeSpan.FromSeconds(15));
    }

    public async Task<Guid> CreateDirectAsync(
        string targetUsername)
    {
        using var response = await Http.PostAsJsonAsync(
            "api/conversations/direct",
            new
            {
                TargetUsername = targetUsername
            });

        await EnsureStatusAsync(
            response,
            HttpStatusCode.Created);

        var conversation = await response.Content
            .ReadFromJsonAsync<ConversationResult>();

        Assert.NotNull(conversation);
        Assert.NotEqual(
            Guid.Empty,
            conversation.ConversationId);

        return conversation.ConversationId;
    }

    public async Task<Guid> CreateGroupAsync(
        string title,
        params string[] memberUsernames)
    {
        using var response = await Http.PostAsJsonAsync(
            "api/conversations/group",
            new
            {
                Title = title,
                MemberUsernames = memberUsernames
            });

        await EnsureStatusAsync(
            response,
            HttpStatusCode.Created);

        var conversation = await response.Content
            .ReadFromJsonAsync<ConversationResult>();

        Assert.NotNull(conversation);
        Assert.NotEqual(
            Guid.Empty,
            conversation.ConversationId);

        return conversation.ConversationId;
    }

    public Task JoinAsync(Guid conversationId)
    {
        return Hub.InvokeAsync(
            "JoinConversation",
            conversationId)
            .WaitAsync(TimeSpan.FromSeconds(15));
    }

    public Task SendAsync(
        Guid conversationId,
        string content)
    {
        return Hub.InvokeAsync(
            "SendMessage",
            conversationId,
            content,
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow)
            .WaitAsync(TimeSpan.FromSeconds(15));
    }

    public Task EditAsync(
        Guid conversationId,
        Guid messageId,
        string content)
    {
        return Hub.InvokeAsync(
            "EditMessage",
            conversationId,
            messageId,
            content)
            .WaitAsync(TimeSpan.FromSeconds(15));
    }

    public Task DeleteAsync(
        Guid conversationId,
        Guid messageId)
    {
        return Hub.InvokeAsync(
            "DeleteMessage",
            conversationId,
            messageId)
            .WaitAsync(TimeSpan.FromSeconds(15));
    }

    public Task<MessageResponse[]> GetHubHistoryAsync(
        Guid conversationId)
    {
        return Hub.InvokeAsync<MessageResponse[]>(
            "GetMessages",
            conversationId,
            50,
            (DateTime?)null,
            (DateTime?)null)
            .WaitAsync(TimeSpan.FromSeconds(15));
    }

    public async Task<MessageResponse[]> GetHistoryAsync(
        Guid conversationId)
    {
        using var response = await Http.GetAsync(
            $"api/conversations/{conversationId}/messages");

        await EnsureStatusAsync(
            response,
            HttpStatusCode.OK);

        return await response.Content
            .ReadFromJsonAsync<MessageResponse[]>()
            ?? throw new InvalidOperationException(
                "Message history response is empty.");
    }

    public async Task<MessageResponse> SendRestAsync(
        Guid conversationId,
        string content)
    {
        using var response = await Http.PostAsJsonAsync(
            $"api/conversations/{conversationId}/messages",
            new
            {
                Content = content,
                ClientMessageId =
                    Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTime.UtcNow
            });

        await EnsureStatusAsync(
            response,
            HttpStatusCode.Created);

        return await response.Content
            .ReadFromJsonAsync<MessageResponse>()
            ?? throw new InvalidOperationException(
                "Message response is empty.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Hub is not null)
                await Hub.DisposeAsync();
        }
        finally
        {
            Http.Dispose();
        }
    }

    public static async Task EnsureStatusAsync(
        HttpResponseMessage response,
        HttpStatusCode expected)
    {
        var body = await response.Content
            .ReadAsStringAsync();

        Assert.True(
            response.StatusCode == expected,
            $"Expected HTTP {(int)expected}, " +
            $"received {(int)response.StatusCode}. " +
            $"Body: {body}");
    }

    public sealed record LoginResult(
        string AccessToken);

    public sealed record ConversationResult(
        Guid ConversationId,
        string Type,
        string DisplayName,
        string? MyRole);

    public sealed record DeletedMessageEvent(
        Guid ConversationId,
        Guid MessageId);
}