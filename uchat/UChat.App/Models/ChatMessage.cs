using System;

namespace UChat.App.Models;

public sealed class ChatMessage
{
    public Guid MessageId { get; set; }
    public Guid ConversationId { get; set; }
    public string SenderAccountId { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ServerReceivedAtUtc { get; set; }
    public DateTime? EditedAtUtc { get; set; }
    public string? ClientMessageId { get; set; }
    public bool IsOwnMessage { get; set; }
}
