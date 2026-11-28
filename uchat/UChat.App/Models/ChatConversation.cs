using System;
using System.Linq;

namespace UChat.App.Models;

public sealed class ChatConversation
{
    public Guid ConversationId { get; set; }

    public string Type { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string? TargetUsername { get; set; }

    // Avatar initials in chat list (up to two letters)
    public string Initials
    {
        get
        {
            var parts = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return "?";
            var letters = parts.Take(2).Select(p => char.ToUpperInvariant(p[0]));
            return string.Concat(letters);
        }
    }
    public string? MyRole { get; set; }

    public bool IsGroup => Type == "Group";

    public bool IsOwner => IsGroup && MyRole == "Owner";

    public string ConversationIcon => IsGroup ? "G" : "D";

    public string Subtitle => IsGroup
        ? $"Group - {MyRole ?? "Member"}"
        : "Direct message";

    public override string ToString() => DisplayName;
}
