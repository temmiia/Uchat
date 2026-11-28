using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using UChat.Application.Messages;
using UChat.Integration.Tests.Infrastructure;
using static UChat.Server.Controllers.ConversationsController;

namespace UChat.Integration.Tests;

public class SignalRIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed record DeleteEventPayload(Guid ConversationId, Guid MessageId);

    [Fact]
    public async Task SignalR_Connect_WithValidToken_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (_, token) = await client.RegisterAndLoginAsync("hub_user_1", "User 1", "Password123!", ct);

        await using var hubConnection = await factory.CreateHubConnectionAsync(token);

        Assert.Equal(HubConnectionState.Connected, hubConnection.State);
    }

    [Fact]
    public async Task SignalR_Connect_WithoutToken_FailsWithHttpRequestException()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();

        var hubUri = new Uri(factory.Server.BaseAddress, "hubs/chat");
        var hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();

        await using (hubConnection)
        {
            await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
            {
                await hubConnection.StartAsync(ct);
            });
        }
    }

    [Fact]
    public async Task SignalR_JoinConversation_AsMember_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (_, tokenA) = await client.RegisterAndLoginAsync("join_user_a", "User A", "Password123!", ct);
        await client.RegisterAndLoginAsync("join_user_b", "User B", "Password123!", ct);

        using var clientA = factory.CreateAuthorizedClient(tokenA);
        var convResp = await clientA.PostAsJsonAsync("/api/conversations/direct", new CreateDirectRequest("join_user_b"), ct);
        var conv = await convResp.Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions, ct);

        await using var hubA = await factory.CreateHubConnectionAsync(tokenA);

        // Should not throw
        await hubA.InvokeAsync("JoinConversation", conv!.ConversationId, ct);
    }

    [Fact]
    public async Task SignalR_JoinConversation_AsNonMember_ThrowsHubException()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (_, tokenA) = await client.RegisterAndLoginAsync("part_a", "Part A", "Password123!", ct);
        await client.RegisterAndLoginAsync("part_b", "Part B", "Password123!", ct);
        var (_, tokenOutsider) = await client.RegisterAndLoginAsync("outsider_hub", "Outsider", "Password123!", ct);

        using var clientA = factory.CreateAuthorizedClient(tokenA);
        var convResp = await clientA.PostAsJsonAsync("/api/conversations/direct", new CreateDirectRequest("part_b"), ct);
        var conv = await convResp.Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions, ct);

        await using var outsiderHub = await factory.CreateHubConnectionAsync(tokenOutsider);

        var ex = await Assert.ThrowsAsync<HubException>(async () =>
        {
            await outsiderHub.InvokeAsync("JoinConversation", conv!.ConversationId, ct);
        });

        Assert.Contains("Access denied", ex.Message);
    }

    [Fact]
    public async Task SignalR_RealTime_SendMessage_DeliversMessageCreatedEventToOtherParticipants()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (accountA, tokenA) = await client.RegisterAndLoginAsync("chat_rt_a", "Alice", "Password123!", ct);
        var (accountB, tokenB) = await client.RegisterAndLoginAsync("chat_rt_b", "Bob", "Password123!", ct);

        using var clientA = factory.CreateAuthorizedClient(tokenA);
        var convResp = await clientA.PostAsJsonAsync("/api/conversations/direct", new CreateDirectRequest("chat_rt_b"), ct);
        var conv = await convResp.Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions, ct);

        await using var hubA = await factory.CreateHubConnectionAsync(tokenA);
        await using var hubB = await factory.CreateHubConnectionAsync(tokenB);

        await hubA.InvokeAsync("JoinConversation", conv!.ConversationId, ct);
        await hubB.InvokeAsync("JoinConversation", conv.ConversationId, ct);

        var messageTcs = new TaskCompletionSource<MessageResponse>();
        hubB.On<MessageResponse>("MessageCreated", msg =>
        {
            messageTcs.TrySetResult(msg);
        });

        // Alice sends message through SignalR hub
        await hubA.InvokeAsync("SendMessage", conv.ConversationId, "Real-time SignalR message!", "rt_client_id_1", null, ct);

        // Bob should receive MessageCreated event
        var received = await messageTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.NotNull(received);
        Assert.Equal("Real-time SignalR message!", received.Content);
        Assert.Equal(accountA, received.SenderAccountId);
        Assert.Equal(conv.ConversationId, received.ConversationId);
    }

    [Fact]
    public async Task SignalR_RealTime_EditMessage_DeliversMessageEditedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (_, tokenA) = await client.RegisterAndLoginAsync("edit_rt_a", "Alice", "Password123!", ct);
        var (_, tokenB) = await client.RegisterAndLoginAsync("edit_rt_b", "Bob", "Password123!", ct);

        using var clientA = factory.CreateAuthorizedClient(tokenA);
        var convResp = await clientA.PostAsJsonAsync("/api/conversations/direct", new CreateDirectRequest("edit_rt_b"), ct);
        var conv = await convResp.Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions, ct);

        // Alice sends an initial message via REST API
        var sendResp = await clientA.PostAsJsonAsync($"/api/conversations/{conv!.ConversationId}/messages", new SendMessageRequest("Initial text"), ct);
        var initialMsg = await sendResp.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions, ct);

        await using var hubA = await factory.CreateHubConnectionAsync(tokenA);
        await using var hubB = await factory.CreateHubConnectionAsync(tokenB);

        await hubA.InvokeAsync("JoinConversation", conv.ConversationId, ct);
        await hubB.InvokeAsync("JoinConversation", conv.ConversationId, ct);

        var editTcs = new TaskCompletionSource<MessageResponse>();
        hubB.On<MessageResponse>("MessageEdited", edited =>
        {
            editTcs.TrySetResult(edited);
        });

        // Alice edits the message via SignalR
        await hubA.InvokeAsync("EditMessage", conv.ConversationId, initialMsg!.MessageId, "SignalR edited text!", ct);

        // Bob receives MessageEdited event
        var receivedEdit = await editTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.NotNull(receivedEdit);
        Assert.Equal(initialMsg.MessageId, receivedEdit.MessageId);
        Assert.Equal("SignalR edited text!", receivedEdit.Content);
    }

    [Fact]
    public async Task SignalR_RealTime_DeleteMessage_DeliversMessageDeletedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (_, tokenA) = await client.RegisterAndLoginAsync("del_rt_a", "Alice", "Password123!", ct);
        var (_, tokenB) = await client.RegisterAndLoginAsync("del_rt_b", "Bob", "Password123!", ct);

        using var clientA = factory.CreateAuthorizedClient(tokenA);
        var convResp = await clientA.PostAsJsonAsync("/api/conversations/direct", new CreateDirectRequest("del_rt_b"), ct);
        var conv = await convResp.Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions, ct);

        // Alice sends an initial message
        var sendResp = await clientA.PostAsJsonAsync($"/api/conversations/{conv!.ConversationId}/messages", new SendMessageRequest("Message to be deleted"), ct);
        var initialMsg = await sendResp.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions, ct);

        await using var hubA = await factory.CreateHubConnectionAsync(tokenA);
        await using var hubB = await factory.CreateHubConnectionAsync(tokenB);

        await hubA.InvokeAsync("JoinConversation", conv.ConversationId, ct);
        await hubB.InvokeAsync("JoinConversation", conv.ConversationId, ct);

        var deleteTcs = new TaskCompletionSource<DeleteEventPayload>();
        hubB.On<DeleteEventPayload>("MessageDeleted", payload =>
        {
            deleteTcs.TrySetResult(payload);
        });

        // Alice deletes the message via SignalR
        await hubA.InvokeAsync("DeleteMessage", conv.ConversationId, initialMsg!.MessageId, ct);

        // Bob receives MessageDeleted event
        var receivedDel = await deleteTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.NotNull(receivedDel);
        Assert.Equal(initialMsg.MessageId, receivedDel.MessageId);
        Assert.Equal(conv.ConversationId, receivedDel.ConversationId);
    }

    [Fact]
    public async Task SignalR_GetMessages_ReturnsPersistedMessagesFromDb()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (_, tokenA) = await client.RegisterAndLoginAsync("history_rt_a", "Alice", "Password123!", ct);
        await client.RegisterAndLoginAsync("history_rt_b", "Bob", "Password123!", ct);

        using var clientA = factory.CreateAuthorizedClient(tokenA);
        var convResp = await clientA.PostAsJsonAsync("/api/conversations/direct", new CreateDirectRequest("history_rt_b"), ct);
        var conv = await convResp.Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions, ct);

        // Send 3 messages
        await clientA.PostAsJsonAsync($"/api/conversations/{conv!.ConversationId}/messages", new SendMessageRequest("Msg 1"), ct);
        await clientA.PostAsJsonAsync($"/api/conversations/{conv.ConversationId}/messages", new SendMessageRequest("Msg 2"), ct);
        await clientA.PostAsJsonAsync($"/api/conversations/{conv.ConversationId}/messages", new SendMessageRequest("Msg 3"), ct);

        await using var hubA = await factory.CreateHubConnectionAsync(tokenA);

        var messages = await hubA.InvokeAsync<IReadOnlyList<MessageResponse>>("GetMessages", conv.ConversationId, 10, null, null, ct);

        Assert.NotNull(messages);
        Assert.Equal(3, messages.Count);
        Assert.Contains(messages, m => m.Content == "Msg 1");
        Assert.Contains(messages, m => m.Content == "Msg 2");
        Assert.Contains(messages, m => m.Content == "Msg 3");
    }

    [Fact]
    public async Task SignalR_EditMessage_ByNonAuthor_ThrowsHubException()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (_, tokenA) = await client.RegisterAndLoginAsync("author_rt", "Author", "Password123!", ct);
        var (_, tokenB) = await client.RegisterAndLoginAsync("hacker_rt", "Hacker", "Password123!", ct);

        using var clientA = factory.CreateAuthorizedClient(tokenA);
        var convResp = await clientA.PostAsJsonAsync("/api/conversations/direct", new CreateDirectRequest("hacker_rt"), ct);
        var conv = await convResp.Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions, ct);

        var sendResp = await clientA.PostAsJsonAsync($"/api/conversations/{conv!.ConversationId}/messages", new SendMessageRequest("Secret author message"), ct);
        var msg = await sendResp.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions, ct);

        await using var hubB = await factory.CreateHubConnectionAsync(tokenB);

        // Hacker tries to edit Author's message via SignalR
        await Assert.ThrowsAsync<HubException>(async () =>
        {
            await hubB.InvokeAsync("EditMessage", conv.ConversationId, msg!.MessageId, "Compromised content", ct);
        });
    }
}
