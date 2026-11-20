using UChat.Application.Abstractions;
using UChat.Domain.Entities;

namespace UChat.Application.Messages;

public sealed class MessageService
{
    private readonly IMessageRepository _messageRepository;

    public MessageService(IMessageRepository messageRepository)
    {
        _messageRepository = messageRepository;
    }

    public async Task<(MessageResponse Message, bool IsDuplicate)> SaveMessageAsync(
        Guid conversationId,
        string senderAccountId,
        string content,
        string? clientMessageId = null,
        DateTime? clientCreatedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException("Invalid conversation ID.", nameof(conversationId));
        }

        if (string.IsNullOrWhiteSpace(senderAccountId))
        {
            throw new ArgumentException("Sender account ID is required.", nameof(senderAccountId));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Message cannot be empty.", nameof(content));
        }

        if (content.Length > 4000)
        {
            throw new ArgumentException("Message cannot exceed 4000 characters.", nameof(content));
        }

        if (clientMessageId is { Length: > 128 })
        {
            throw new ArgumentException("Client message ID is too long.", nameof(clientMessageId));
        }

        // 1. Idempotency check: if clientMessageId is supplied, check if already processed
        if (!string.IsNullOrWhiteSpace(clientMessageId))
        {
            var existing = await _messageRepository.GetByClientMessageIdAsync(
                conversationId,
                senderAccountId,
                clientMessageId,
                cancellationToken);

            if (existing is not null)
            {
                // Message with this clientMessageId was already persisted. Return existing without duplication.
                return (ToResponse(existing), true);
            }
        }

        // 2. Offline timestamp support: preserve client's creation timestamp if provided (prevent future dates)
        var serverNow = DateTime.UtcNow;
        var createdAt = clientCreatedAtUtc.HasValue && clientCreatedAtUtc.Value <= serverNow.AddMinutes(5)
            ? clientCreatedAtUtc.Value
            : serverNow;

        var message = new Message(
            conversationId,
            senderAccountId,
            content,
            clientMessageId,
            createdAtUtc: createdAt)
        {
            ServerReceivedAtUtc = serverNow
        };

        var inserted = await _messageRepository.CreateAsync(message, cancellationToken);
        if (!inserted && !string.IsNullOrWhiteSpace(clientMessageId))
        {
            // If duplicate was detected concurrently, retrieve and return the existing record
            var existing = await _messageRepository.GetByClientMessageIdAsync(
                conversationId,
                senderAccountId,
                clientMessageId,
                cancellationToken);

            if (existing is not null)
            {
                return (ToResponse(existing), true);
            }
        }

        return (ToResponse(message), false);
    }

    public async Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(
        Guid conversationId,
        int limit = 50,
        DateTime? beforeUtc = null,
        DateTime? afterUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException("Invalid conversation ID.", nameof(conversationId));
        }

        if (limit is <= 0 or > 100)
        {
            limit = 50;
        }

        var messages = await _messageRepository.GetByConversationIdAsync(
            conversationId,
            limit,
            beforeUtc,
            afterUtc,
            cancellationToken);

        return messages.Select(ToResponse).ToList();
    }

    public async Task<MessageResponse?> GetByIdAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var message = await _messageRepository.GetByIdAsync(messageId, cancellationToken);
        return message is null ? null : ToResponse(message);
    }

    private static MessageResponse ToResponse(Message m) =>
        new(
            m.MessageId,
            m.ConversationId,
            m.SenderAccountId,
            m.Content,
            m.CreatedAtUtc,
            m.EditedAtUtc,
            m.ClientMessageId,
            m.ServerReceivedAtUtc);
}
