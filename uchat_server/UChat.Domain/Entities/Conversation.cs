using UChat.Domain.Enums;

namespace UChat.Domain.Entities;

// Модель беседы (личный диалог 1-на-1 или групповой чат)
public class Conversation
{
    // Пустой конструктор для EF Core
    private Conversation()
    {
    }

    public Conversation(ConversationType type, string? title = null, Guid? conversationId = null)
    {
        // Для групповых чатов название обязательно
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

    // Direct или Group
    public ConversationType Type { get; set; } = ConversationType.Direct;

    public string? Title { get; set; }

    public DateTime CreatedAtUtc { get; private init; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<ConversationMember> Members { get; set; } = new List<ConversationMember>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
