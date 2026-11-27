using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UChat.Infrastructure.Data;
using UChat.Application.Messages;
using UChat.Server.Services;

namespace UChat.Server.Hubs;

[Authorize]
public sealed class ChatHub : Hub
{
    private readonly AppDbContext _dbContext;
    private readonly MessageService _messageService;
    private readonly ChatEventPublisher _eventPublisher;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        AppDbContext dbContext,
        MessageService messageService,
        ChatEventPublisher eventPublisher,
        ILogger<ChatHub> logger)
    {
        _dbContext = dbContext;
        _messageService = messageService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    private string? AccountId =>
        Context.User?.FindFirstValue(
            JwtRegisteredClaimNames.Sub)
        ?? Context.User?.FindFirstValue(
            ClaimTypes.NameIdentifier);

    private void EnsureNotExpired()
    {
        var expClaim = Context.User?.FindFirst(JwtRegisteredClaimNames.Exp)?.Value
            ?? Context.User?.FindFirst("exp")?.Value;

        if (!string.IsNullOrWhiteSpace(expClaim) && long.TryParse(expClaim, out var expSeconds))
        {
            var expTime = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
            if (expTime <= DateTimeOffset.UtcNow)
            {
                _logger.LogWarning("Security event: Expired token attempt by Account {AccountId} on Connection {ConnectionId}", AccountId, Context.ConnectionId);
                Context.Abort();
                throw new HubException("Token has expired. Please re-authenticate.");
            }
        }
    }

    public override async Task OnConnectedAsync()
    {
        EnsureNotExpired();
        var accountId = AccountId;

        if (string.IsNullOrWhiteSpace(accountId))
        {
            _logger.LogWarning("Unauthenticated SignalR connection attempt aborted: {ConnectionId}", Context.ConnectionId);
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            GetAccountGroup(accountId),
            Context.ConnectionAborted);

        _logger.LogInformation("SignalR client {ConnectionId} connected for account {AccountId}", Context.ConnectionId, accountId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var accountId = AccountId;
        if (exception is not null)
        {
            _logger.LogWarning(exception, "SignalR client {ConnectionId} disconnected with error for account {AccountId}", Context.ConnectionId, accountId);
        }
        else
        {
            _logger.LogInformation("SignalR client {ConnectionId} disconnected for account {AccountId}", Context.ConnectionId, accountId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public static string GetAccountGroup(string accountId)
    {
        return $"account:{accountId}";
    }

    public static string GetConversationGroup(
        Guid conversationId)
    {
        return $"conversation:{conversationId:D}";
    }

    public async Task JoinConversation(
        Guid conversationId)
    {
        EnsureNotExpired();
        var accountId = AccountId;

        if (string.IsNullOrWhiteSpace(accountId))
            throw new HubException("Unauthorized.");

        if (conversationId == Guid.Empty)
            throw new HubException("Invalid conversation ID.");

        var isMember = await _dbContext.Members
            .AsNoTracking()
            .AnyAsync(
                m => m.ConversationId == conversationId
                     && m.AccountId == accountId,
                Context.ConnectionAborted);

        if (!isMember)
        {
            _logger.LogWarning("Security event: SignalR JoinConversation denied for Account {AccountId} to {ConversationId}", accountId, conversationId);
            throw new HubException(
                "Access denied to this conversation.");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            GetConversationGroup(conversationId),
            Context.ConnectionAborted);

        _logger.LogInformation("Account {AccountId} joined conversation {ConversationId}", accountId, conversationId);
    }

    public async Task LeaveConversation(
        Guid conversationId)
    {
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            GetConversationGroup(conversationId));

        _logger.LogInformation("Account {AccountId} left conversation {ConversationId}", AccountId, conversationId);
    }

    public async Task SendMessage(
        Guid conversationId,
        string content,
        string? clientMessageId = null,
        DateTime? createdAtUtc = null)
    {
        EnsureNotExpired();
        var accountId = AccountId;

        if (string.IsNullOrWhiteSpace(accountId))
            throw new HubException("Unauthorized.");

        if (conversationId == Guid.Empty)
            throw new HubException("Invalid conversation ID.");

        if (string.IsNullOrWhiteSpace(content))
            throw new HubException("Message cannot be empty.");

        if (content.Length > 4000)
            throw new HubException(
                "Message cannot exceed 4000 characters.");

        if (clientMessageId is { Length: > 128 })
            throw new HubException(
                "Client message ID is too long.");

        var memberIds = await _dbContext.Members
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .Select(m => m.AccountId)
            .ToListAsync(Context.ConnectionAborted);

        if (!memberIds.Contains(accountId))
        {
            _logger.LogWarning("Security event: SignalR SendMessage denied for Account {AccountId} to conversation {ConversationId}", accountId, conversationId);
            throw new HubException(
                "Access denied to this conversation.");
        }

        var (response, isDuplicate) =
            await _messageService.SaveMessageAsync(
                conversationId,
                accountId,
                content,
                clientMessageId,
                createdAtUtc,
                Context.ConnectionAborted);

        if (isDuplicate)
            return;

        var messageEntity = await _dbContext.Messages
    .AsNoTracking()
    .Include(m => m.Sender)
    .FirstOrDefaultAsync(
        m => m.MessageId == response.MessageId,
        Context.ConnectionAborted);

        if (messageEntity is null)
            return;

        await _eventPublisher.PublishMessageCreatedAsync(
            messageEntity,
            Context.ConnectionAborted);

        await _eventPublisher.PublishConversationActivityAsync(
            conversationId,
            memberIds,
            Context.ConnectionAborted);
    }

    public async Task EditMessage(
        Guid conversationId,
        Guid messageId,
        string newContent)
    {
        EnsureNotExpired();
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

        var isMember = await _dbContext.Members
            .AsNoTracking()
            .AnyAsync(member =>
                member.ConversationId == conversationId &&
                member.AccountId == accountId,
                Context.ConnectionAborted);

        if (!isMember)
        {
            _logger.LogWarning("Security event: SignalR EditMessage denied for Account {AccountId} to conversation {ConversationId}", accountId, conversationId);
            throw new HubException("Access denied to this conversation.");
        }

        try
        {
            var response = await _messageService.EditMessageAsync(
                conversationId,
                messageId,
                accountId,
                newContent,
                Context.ConnectionAborted);

            var messageEntity = await _dbContext.Messages
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.MessageId == response.MessageId, Context.ConnectionAborted);

            if (messageEntity is not null)
            {
                await _eventPublisher.PublishMessageEditedAsync(
                    messageEntity,
                    Context.ConnectionAborted);
            }
        }
        catch (ArgumentException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public async Task DeleteMessage(
        Guid conversationId,
        Guid messageId)
    {
        EnsureNotExpired();
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

        var isMember = await _dbContext.Members
            .AsNoTracking()
            .AnyAsync(member =>
                member.ConversationId == conversationId &&
                member.AccountId == accountId,
                Context.ConnectionAborted);

        if (!isMember)
        {
            _logger.LogWarning("Security event: SignalR DeleteMessage denied for Account {AccountId} to conversation {ConversationId}", accountId, conversationId);
            throw new HubException("Access denied to this conversation.");
        }

        try
        {
            await _messageService.DeleteMessageAsync(
                conversationId,
                messageId,
                accountId,
                Context.ConnectionAborted);

            await _eventPublisher.PublishMessageDeletedAsync(
                conversationId,
                messageId,
                Context.ConnectionAborted);
        }
        catch (ArgumentException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public async Task<IReadOnlyList<MessageResponse>>
        GetMessages(
            Guid conversationId,
            int limit = 50,
            DateTime? beforeUtc = null,
            DateTime? afterUtc = null)
    {
        EnsureNotExpired();
        var accountId = AccountId;

        if (string.IsNullOrWhiteSpace(accountId))
            throw new HubException("Unauthorized.");

        if (conversationId == Guid.Empty)
            throw new HubException("Invalid conversation ID.");

        var isMember = await _dbContext.Members
            .AsNoTracking()
            .AnyAsync(
                m => m.ConversationId == conversationId
                     && m.AccountId == accountId,
                Context.ConnectionAborted);

        if (!isMember)
        {
            _logger.LogWarning("Security event: SignalR GetMessages denied for Account {AccountId} to conversation {ConversationId}", accountId, conversationId);
            throw new HubException(
                "Access denied to this conversation.");
        }

        return await _messageService.GetMessagesAsync(
            conversationId,
            limit,
            beforeUtc,
            afterUtc,
            Context.ConnectionAborted);
    }
}
