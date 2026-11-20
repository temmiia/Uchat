namespace UChat.Domain.Entities;

// User domain entity
public class User
{
    // Parameterless constructor for EF Core
    private User()
    {
    }

    public User(string accountId, string userName, string displayName, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException("AccountId cannot be null or whitespace.", nameof(accountId));
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("UserName cannot be null or whitespace.", nameof(userName));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("DisplayName cannot be null or whitespace.", nameof(displayName));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("PasswordHash cannot be null or whitespace.", nameof(passwordHash));
        }

        AccountId = accountId;
        UserName = userName;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        CreatedAtUtc = DateTime.UtcNow;
    }

    // 1st layer of defense: AccountId is set only upon registration (private init) and has no public setter
    public string AccountId { get; private init; } = string.Empty;

    // Public unique username handle (@username)
    public string UserName { get; set; } = string.Empty;

    // Human-readable display name in chat
    public string DisplayName { get; set; } = string.Empty;

    // Secure password hash (never stored in plain text)
    public string PasswordHash { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }

    public DateTime CreatedAtUtc { get; private init; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    // Navigation properties
    public ICollection<ConversationMember> Memberships { get; set; } = new List<ConversationMember>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
