using UChat.Domain.Enums;

namespace UChat.Domain.Entities;

// Модель участника беседы (связь многие-ко-многим между User и Conversation)
public class ConversationMember
{
    // Пустой конструктор для EF Core
    private ConversationMember()
    {
    }

    public ConversationMember(Guid conversationId, string accountId, MemberRole role = MemberRole.Member, Guid? memberId = null)
    {
        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException("ConversationId cannot be empty.", nameof(conversationId));
        }

        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException("AccountId cannot be null or whitespace.", nameof(accountId));
        }

        MemberId = memberId ?? Guid.NewGuid();
        ConversationId = conversationId;
        AccountId = accountId;
        Role = role;
        JoinedAtUtc = DateTime.UtcNow;
    }

    public Guid MemberId { get; private init; } = Guid.NewGuid();

    // Внешние ключи
    public Guid ConversationId { get; set; }
    public string AccountId { get; set; } = string.Empty;

    // Роль: Owner, Admin, Member
    public MemberRole Role { get; set; } = MemberRole.Member;

    public DateTime JoinedAtUtc { get; private init; } = DateTime.UtcNow;

    // Навигационные свойства
    public Conversation Conversation { get; set; } = null!;
    public User User { get; set; } = null!;
}
