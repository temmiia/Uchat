using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UChat.Application.Messages;
using UChat.Domain.Enums;
using UChat.Integration.Tests.Infrastructure;
using static UChat.Server.Controllers.ConversationsController;

namespace UChat.Integration.Tests;

public class ConversationsAndMessagesApiIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CreateDirectConversation_PersistsConversationAndMembersInDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (accountA, tokenA) = await client.RegisterAndLoginAsync("chat_user_a", "User A", "Password123!", ct);
        var (accountB, _) = await client.RegisterAndLoginAsync("chat_user_b", "User B", "Password123!", ct);

        using var clientA = factory.CreateAuthorizedClient(tokenA);

        var response = await clientA.PostAsJsonAsync("/api/conversations/direct", new CreateDirectRequest("chat_user_b"), ct);
        Assert.True(response.IsSuccessStatusCode);

        var conv = await response.Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions, ct);
        Assert.NotNull(conv);
        Assert.NotEqual(Guid.Empty, conv.ConversationId);

        // Verify SQLite database
        using var db = factory.CreateDbContext();
        var convInDb = await db.Conversations
            .Include(c => c.Members)
            .SingleOrDefaultAsync(c => c.ConversationId == conv.ConversationId, ct);

        Assert.NotNull(convInDb);
        Assert.Equal(ConversationType.Direct, convInDb.Type);
        Assert.Equal(2, convInDb.Members.Count);
        Assert.Contains(convInDb.Members, m => m.AccountId == accountA);
        Assert.Contains(convInDb.Members, m => m.AccountId == accountB);
    }

    [Fact]
    public async Task SendMessage_PersistsMessageInDb_WithUtcTimestamp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (accountA, tokenA) = await client.RegisterAndLoginAsync("msg_sender_a", "Sender A", "Password123!", ct);
        await client.RegisterAndLoginAsync("msg_receiver_b", "Receiver B", "Password123!", ct);

        using var clientA = factory.CreateAuthorizedClient(tokenA);
        var convResp = await clientA.PostAsJsonAsync("/api/conversations/direct", new CreateDirectRequest("msg_receiver_b"), ct);
        var conv = await convResp.Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions, ct);

        var sendRequest = new SendMessageRequest("Hello from integration test!", "client_msg_101");
        var msgResp = await clientA.PostAsJsonAsync($"/api/conversations/{conv!.ConversationId}/messages", sendRequest, ct);

        Assert.Equal(HttpStatusCode.Created, msgResp.StatusCode);
        var msg = await msgResp.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions, ct);
        Assert.NotNull(msg);
        Assert.Equal("Hello from integration test!", msg.Content);
        Assert.Equal(accountA, msg.SenderAccountId);
        Assert.Equal("client_msg_101", msg.ClientMessageId);

        // Verify database persistence
        using var db = factory.CreateDbContext();
        var msgInDb = await db.Messages.SingleOrDefaultAsync(m => m.MessageId == msg.MessageId, ct);
        Assert.NotNull(msgInDb);
        Assert.Equal("Hello from integration test!", msgInDb.Content);
        Assert.Equal(DateTimeKind.Utc, msgInDb.CreatedAtUtc.Kind);
        Assert.False(msgInDb.IsDeleted);
    }

    [Fact]
    public async Task MessageIdempotency_DuplicateClientMessageId_DoesNotCreateDuplicateInDb()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (_, tokenA) = await client.RegisterAndLoginAsync("idem_user_a", "User A", "Password123!", ct);
        await client.RegisterAndLoginAsync("idem_user_b", "User B", "Password123!", ct);

        using var clientA = factory.CreateAuthorizedClient(tokenA);
        var convResp = await clientA.PostAsJsonAsync("/api/conversations/direct", new CreateDirectRequest("idem_user_b"), ct);
        var conv = await convResp.Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions, ct);

        var firstSend = new SendMessageRequest("Idempotent message", "duplicate_client_id_999");
        var resp1 = await clientA.PostAsJsonAsync($"/api/conversations/{conv!.ConversationId}/messages", firstSend, ct);
        Assert.True(resp1.IsSuccessStatusCode);
        var msg1 = await resp1.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions, ct);

        // Send second time with identical ClientMessageId
        var resp2 = await clientA.PostAsJsonAsync($"/api/conversations/{conv.ConversationId}/messages", firstSend, ct);
        Assert.True(resp2.IsSuccessStatusCode);
        var msg2 = await resp2.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions, ct);

        // Must return the exact same message
        Assert.Equal(msg1!.MessageId, msg2!.MessageId);

        // Database must have only 1 row
        using var db = factory.CreateDbContext();
        var count = await db.Messages.CountAsync(m => m.ConversationId == conv.ConversationId && m.ClientMessageId == "duplicate_client_id_999", ct);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task NonMember_AccessingConversation_Returns403Forbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (_, tokenA) = await client.RegisterAndLoginAsync("member_a", "Member A", "Password123!", ct);
        await client.RegisterAndLoginAsync("member_b", "Member B", "Password123!", ct);
        var (_, tokenC) = await client.RegisterAndLoginAsync("outsider_c", "Outsider C", "Password123!", ct);

        using var clientA = factory.CreateAuthorizedClient(tokenA);
        var convResp = await clientA.PostAsJsonAsync("/api/conversations/direct", new CreateDirectRequest("member_b"), ct);
        var conv = await convResp.Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions, ct);

        using var clientC = factory.CreateAuthorizedClient(tokenC);

        // Outsider tries to read messages
        var getResp = await clientC.GetAsync($"/api/conversations/{conv!.ConversationId}/messages", ct);
        Assert.Equal(HttpStatusCode.Forbidden, getResp.StatusCode);

        // Outsider tries to send message
        var postResp = await clientC.PostAsJsonAsync($"/api/conversations/{conv.ConversationId}/messages", new SendMessageRequest("Intrusion"), ct);
        Assert.Equal(HttpStatusCode.Forbidden, postResp.StatusCode);
    }

    [Fact]
    public async Task EditMessage_ByAuthor_UpdatesContentAndTimestamp_ByNonAuthor_Returns403Forbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (_, tokenA) = await client.RegisterAndLoginAsync("author_alice", "Alice", "Password123!", ct);
        var (_, tokenB) = await client.RegisterAndLoginAsync("member_bob", "Bob", "Password123!", ct);

        using var clientA = factory.CreateAuthorizedClient(tokenA);
        using var clientB = factory.CreateAuthorizedClient(tokenB);

        var convResp = await clientA.PostAsJsonAsync("/api/conversations/direct", new CreateDirectRequest("member_bob"), ct);
        var conv = await convResp.Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions, ct);

        var msgResp = await clientA.PostAsJsonAsync($"/api/conversations/{conv!.ConversationId}/messages", new SendMessageRequest("Original text"), ct);
        var msg = await msgResp.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions, ct);

        // Bob (non-author) attempts to edit Alice's message -> 403 Forbidden
        var forbiddenEditResp = await clientB.PutAsJsonAsync($"/api/conversations/{conv.ConversationId}/messages/{msg!.MessageId}", new EditMessageRequest("Hacked text"), ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenEditResp.StatusCode);

        // Verify DB unchanged
        using (var db = factory.CreateDbContext())
        {
            var msgInDb = await db.Messages.SingleAsync(m => m.MessageId == msg.MessageId, ct);
            Assert.Equal("Original text", msgInDb.Content);
            Assert.Null(msgInDb.EditedAtUtc);
        }

        // Alice (author) edits her message -> 200 OK
        var okEditResp = await clientA.PutAsJsonAsync($"/api/conversations/{conv.ConversationId}/messages/{msg.MessageId}", new EditMessageRequest("Updated text"), ct);
        Assert.Equal(HttpStatusCode.OK, okEditResp.StatusCode);

        var updatedMsg = await okEditResp.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions, ct);
        Assert.Equal("Updated text", updatedMsg!.Content);
        Assert.NotNull(updatedMsg.EditedAtUtc);

        // Verify DB updated
        using (var db = factory.CreateDbContext())
        {
            var msgInDb = await db.Messages.SingleAsync(m => m.MessageId == msg.MessageId, ct);
            Assert.Equal("Updated text", msgInDb.Content);
            Assert.NotNull(msgInDb.EditedAtUtc);
        }
    }

    [Fact]
    public async Task DeleteMessage_ByAuthor_SoftDeletesInDb_AndExcludesFromHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (_, tokenA) = await client.RegisterAndLoginAsync("del_author_a", "Alice", "Password123!", ct);
        var (_, tokenB) = await client.RegisterAndLoginAsync("del_member_b", "Bob", "Password123!", ct);

        using var clientA = factory.CreateAuthorizedClient(tokenA);
        using var clientB = factory.CreateAuthorizedClient(tokenB);

        var convResp = await clientA.PostAsJsonAsync("/api/conversations/direct", new CreateDirectRequest("del_member_b"), ct);
        var conv = await convResp.Content.ReadFromJsonAsync<ConversationResponse>(JsonOptions, ct);

        var msgResp = await clientA.PostAsJsonAsync($"/api/conversations/{conv!.ConversationId}/messages", new SendMessageRequest("Message to delete"), ct);
        var msg = await msgResp.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions, ct);

        // Bob (non-author) attempts to delete -> 403 Forbidden
        var forbiddenDelResp = await clientB.DeleteAsync($"/api/conversations/{conv.ConversationId}/messages/{msg!.MessageId}", ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelResp.StatusCode);

        // Alice (author) deletes -> 204 No Content
        var okDelResp = await clientA.DeleteAsync($"/api/conversations/{conv.ConversationId}/messages/{msg.MessageId}", ct);
        Assert.Equal(HttpStatusCode.NoContent, okDelResp.StatusCode);

        // Verify DB row has soft-delete timestamp and blank content
        using (var db = factory.CreateDbContext())
        {
            var msgInDb = await db.Messages.SingleAsync(m => m.MessageId == msg.MessageId, ct);
            Assert.True(msgInDb.IsDeleted);
            Assert.Equal(string.Empty, msgInDb.Content);
        }

        // Verify message history excludes the soft-deleted message
        var historyResp = await clientA.GetAsync($"/api/conversations/{conv.ConversationId}/messages", ct);
        var history = await historyResp.Content.ReadFromJsonAsync<IReadOnlyList<MessageResponse>>(JsonOptions, ct);
        Assert.NotNull(history);
        Assert.DoesNotContain(history, m => m.MessageId == msg.MessageId);
    }
}
