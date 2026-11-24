using System;
using System.Linq;

namespace UChat.App.Models;

public sealed class ChatConversation
{
    public Guid ConversationId { get; set; }
    public string Type { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? TargetUsername { get; set; }

    // Ініціали для аватара в списку чатів (до двох літер)
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

    public override string ToString() => DisplayName;
}
