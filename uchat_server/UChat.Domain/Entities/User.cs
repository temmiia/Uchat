namespace UChat.Domain.Entities;

// Модель пользователя
public class User
{
    // Пустой конструктор для EF Core
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

    // 1-й уровень защиты: AccountId задается только при создании и не имеет публичного сеттера
    public string AccountId { get; private init; } = string.Empty;

    // Публичный никнейм для поиска и входа (@username)
    public string UserName { get; set; } = string.Empty;

    // Отображаемое имя в чате
    public string DisplayName { get; set; } = string.Empty;

    // Хеш пароля (в открытом виде не храним)
    public string PasswordHash { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }

    public DateTime CreatedAtUtc { get; private init; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    // Навигационные свойства
    public ICollection<ConversationMember> Memberships { get; set; } = new List<ConversationMember>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
