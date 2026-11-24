using UChat.Domain.Entities;

namespace UChat.Application.Abstractions;

public interface IMessageRepository
{
    Task<bool> CreateAsync(
        Message message,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Message>> GetByConversationIdAsync(
        Guid conversationId,
        int limit = 50,
        DateTime? beforeUtc = null,
        DateTime? afterUtc = null,
        CancellationToken cancellationToken = default);

    Task<Message?> GetByIdAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task<Message?> GetByClientMessageIdAsync(
        Guid conversationId,
        string senderAccountId,
        string clientMessageId,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Message message,
        CancellationToken cancellationToken = default);
}
