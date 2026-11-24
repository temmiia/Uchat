using System;

namespace UChat.App.Models;

public sealed class ChatConversation
{
    public Guid ConversationId { get; set; }

    public string Type { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string? TargetUsername { get; set; }

    public string? MyRole { get; set; }

    public bool IsGroup => Type == "Group";

    public bool IsOwner => IsGroup && MyRole == "Owner";

    public string ConversationIcon => IsGroup ? "G" : "D";

    public string Subtitle => IsGroup
        ? $"Group - {MyRole ?? "Member"}"
        : "Direct message";

    public override string ToString() => DisplayName;
}
