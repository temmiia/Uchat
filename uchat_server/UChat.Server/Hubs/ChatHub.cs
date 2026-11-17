using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UChat.Infrastructure.Data;
using System.IdentityModel.Tokens.Jwt;
using UChat.Domain.Entities;
using UChat.Server.Services;

namespace UChat.Server.Hubs;

[Authorize]
public sealed class ChatHub : Hub
{
    private readonly AppDbContext _dbContext;
    private readonly ChatEventPublisher _eventPublisher;

    public ChatHub(
        AppDbContext dbContext,
        ChatEventPublisher eventPublisher)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
    }
    public async Task JoinConversation(Guid conversationId)
    {
        var accountId = Context.User?.FindFirstValue(
            JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new HubException("Unauthorized.");
        }

        if (conversationId == Guid.Empty)
        {
            throw new HubException("Invalid conversation ID.");
        }

        // Verify membership before joining a conversation group.
        var isMember = await _dbContext.Members
            .AsNoTracking()
            .AnyAsync(member =>
                member.ConversationId == conversationId &&
                member.AccountId == accountId,
                Context.ConnectionAborted);

        if (!isMember)
        {
            throw new HubException("Access denied to this conversation.");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            GetConversationGroup(conversationId),
            Context.ConnectionAborted);
    }

    public async Task LeaveConversation(Guid conversationId)
    {
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            GetConversationGroup(conversationId));
    }
    public async Task SendMessage(
    Guid conversationId,
    string content,
    string? clientMessageId = null)
    {
        var accountId = Context.User?.FindFirst(
            JwtRegisteredClaimNames.Sub)?.Value;

        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new HubException("Unauthorized.");
        }

        if (conversationId == Guid.Empty)
        {
            throw new HubException("Invalid conversation ID.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new HubException("Message cannot be empty.");
        }
        // Prevent excessively large messages.
        if (content.Length > 4000)
        {
            throw new HubException("Message cannot exceed 4000 characters.");
        }

        // Validate the optional client-generated message identifier.
        if (clientMessageId is { Length: > 128 })
        {
            throw new HubException("Client message ID is too long.");
        }

        // Verify membership before allowing the user to send a message.
        var isMember = await _dbContext.Members
            .AsNoTracking()
            .AnyAsync(member =>
                member.ConversationId == conversationId &&
                member.AccountId == accountId,
                Context.ConnectionAborted);

        if (!isMember)
        {
            throw new HubException("Access denied to this conversation.");
        }

        var message = new Message(
        conversationId,
        accountId,
        content,
        clientMessageId);

        _dbContext.Messages.Add(message);

        // Persist the message before notifying connected clients.
        await _dbContext.SaveChangesAsync(Context.ConnectionAborted);

        await _eventPublisher.PublishMessageCreatedAsync(
            message,
            Context.ConnectionAborted);
    }
    public static string GetConversationGroup(Guid conversationId)
    {
        return $"conversation:{conversationId:D}";
    }
}
