using Microsoft.AspNetCore.SignalR;
using UChat.Domain.Entities;
using UChat.Server.Hubs;

namespace UChat.Server.Services;

public sealed class ChatEventPublisher
{
    private readonly IHubContext<ChatHub> _hubContext;

    public ChatEventPublisher(
        IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task PublishMessageCreatedAsync(
        Message message,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group(ChatHub.GetConversationGroup(
                message.ConversationId))
            .SendAsync(
                "MessageCreated",
                new
                {
                    message.MessageId,
                    message.ConversationId,
                    message.SenderAccountId,
                    message.Content,
                    message.CreatedAtUtc,
                    message.EditedAtUtc,
                    message.ServerReceivedAtUtc,
                    message.ClientMessageId
                },
                cancellationToken);
    }

    public async Task PublishMessageEditedAsync(
        Message message,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group(ChatHub.GetConversationGroup(
                message.ConversationId))
            .SendAsync(
                "MessageEdited",
                new
                {
                    message.MessageId,
                    message.ConversationId,
                    message.SenderAccountId,
                    message.Content,
                    message.CreatedAtUtc,
                    message.EditedAtUtc,
                    message.ServerReceivedAtUtc,
                    message.ClientMessageId
                },
                cancellationToken);
    }

    public async Task PublishMessageDeletedAsync(
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group(ChatHub.GetConversationGroup(
                conversationId))
            .SendAsync(
                "MessageDeleted",
                new
                {
                    ConversationId = conversationId,
                    MessageId = messageId
                },
                cancellationToken);
    }

    public Task PublishConversationCreatedAsync(
        string first,
        string second,
        Guid conversationId)
    {
        return _hubContext.Clients
            .Users(new[] { first, second })
            .SendAsync(
                "ConversationCreated",
                conversationId);
    }

    public Task PublishConversationActivityAsync(
        Guid conversationId,
        IReadOnlyList<string> accountIds,
        CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients
            .Users(accountIds)
            .SendAsync(
                "ConversationCreated",
                conversationId,
                cancellationToken);
    }
}
