namespace UChat.Application.Messages;

public sealed record MessageResponse(
    Guid MessageId,
    Guid ConversationId,
    string SenderAccountId,
    string Content,
    DateTime CreatedAtUtc,
    DateTime? EditedAtUtc,
    string? ClientMessageId,
    DateTime? ServerReceivedAtUtc = null,
    string? SenderDisplayName = null);
