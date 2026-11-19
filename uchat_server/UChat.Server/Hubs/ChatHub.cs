using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UChat.Infrastructure.Data;
using System.IdentityModel.Tokens.Jwt;
using UChat.Application.Messages;
using UChat.Domain.Entities;
using UChat.Server.Services;

namespace UChat.Server.Hubs;

[Authorize]
public sealed class ChatHub : Hub
{
    private readonly AppDbContext _dbContext;
    private readonly MessageService _messageService;
    private readonly ChatEventPublisher _eventPublisher;

    public ChatHub(
        AppDbContext dbContext,
        MessageService messageService,
        ChatEventPublisher eventPublisher)
    {
        _dbContext = dbContext;
        _messageService = messageService;
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
        string? clientMessageId = null,
        DateTime? createdAtUtc = null)
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

        if (content.Length > 4000)
        {
            throw new HubException("Message cannot exceed 4000 characters.");
        }

        if (clientMessageId is { Length: > 128 })
        {
            throw new HubException("Client message ID is too long.");
        }

        // Verify membership before allowing user to send message
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

        // Save message with idempotency check against duplicate ClientMessageId
        var (response, isDuplicate) = await _messageService.SaveMessageAsync(
            conversationId,
            accountId,
            content,
            clientMessageId,
            createdAtUtc,
            Context.ConnectionAborted);

        // Only broadcast if it is a new message (not a retransmitted duplicate)
        if (!isDuplicate)
        {
            var messageEntity = await _dbContext.Messages
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.MessageId == response.MessageId, Context.ConnectionAborted);

            if (messageEntity is not null)
            {
                await _eventPublisher.PublishMessageCreatedAsync(
                    messageEntity,
                    Context.ConnectionAborted);
            }
        }
    }

    public async Task<IReadOnlyList<MessageResponse>> GetMessages(
        Guid conversationId,
        int limit = 50,
        DateTime? beforeUtc = null,
        DateTime? afterUtc = null)
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

        // Verify membership before loading history
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

        // Returns either historical (beforeUtc) or reconnect catch-up (afterUtc) messages
        return await _messageService.GetMessagesAsync(
            conversationId,
            limit,
            beforeUtc,
            afterUtc,
            Context.ConnectionAborted);
    }

    public static string GetConversationGroup(Guid conversationId)
    {
        return $"conversation:{conversationId:D}";
    }
}
