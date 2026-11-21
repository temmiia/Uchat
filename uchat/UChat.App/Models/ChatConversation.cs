
using System;

namespace UChat.App.Models;

public sealed class ChatConversation
{
    public Guid ConversationId { get; set; }
    public string Type { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? TargetUsername { get; set; }

    public override string ToString() => DisplayName;
}
