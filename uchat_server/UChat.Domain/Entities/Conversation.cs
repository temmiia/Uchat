using UChat.Domain.Enums;

namespace UChat.Domain.Entities;

// Conversation entity (1-on-1 direct dialogue or group chat)
public class Conversation
{
    // Parameterless constructor for EF Core
    private Conversation()
    {
    }

    public Conversation(ConversationType type, string? title = null, Guid? conversationId = null)
    {
        // Group chats require a non-empty title
        if (type == ConversationType.Group && string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Group conversation must have a non-empty title.", nameof(title));
        }

        ConversationId = conversationId ?? Guid.NewGuid();
        Type = type;
        Title = title?.Trim();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid ConversationId { get; private init; } = Guid.NewGuid();

    // Direct or Group
    public ConversationType Type { get; set; } = ConversationType.Direct;

    public string? Title { get; set; }

    public DateTime CreatedAtUtc { get; private init; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<ConversationMember> Members { get; set; } = new List<ConversationMember>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
