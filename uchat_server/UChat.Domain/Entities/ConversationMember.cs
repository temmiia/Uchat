using UChat.Domain.Enums;

namespace UChat.Domain.Entities;

// Membership relationship between a User and a Conversation
public class ConversationMember
{
    // Parameterless constructor for EF Core
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

    // Foreign keys
    public Guid ConversationId { get; set; }
    public string AccountId { get; set; } = string.Empty;

    // Role: Owner, Admin, Member
    public MemberRole Role { get; set; } = MemberRole.Member;

    public DateTime JoinedAtUtc { get; private init; } = DateTime.UtcNow;

    // Navigation properties
    public Conversation Conversation { get; set; } = null!;
    public User User { get; set; } = null!;
}
