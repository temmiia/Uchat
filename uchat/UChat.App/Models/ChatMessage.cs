using System;
using System.Text.Json.Serialization;
using UChat.App.Services;

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
    [JsonIgnore]
    public bool IsOwnMessage => !string.IsNullOrWhiteSpace(AuthSession.AccountId)
        && string.Equals(SenderAccountId, AuthSession.AccountId, StringComparison.Ordinal);
    [JsonIgnore] public bool IsEdited => EditedAtUtc.HasValue;
    [JsonIgnore] public string SenderLabel { get; set; } = "";
    [JsonIgnore] public string DisplayTime => DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm");
}
