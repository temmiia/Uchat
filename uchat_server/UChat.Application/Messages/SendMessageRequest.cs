namespace UChat.Application.Messages;

public sealed record SendMessageRequest(
    string Content,
    string? ClientMessageId = null,
    DateTime? CreatedAtUtc = null);
