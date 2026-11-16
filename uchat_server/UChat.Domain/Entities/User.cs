namespace UChat.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Username { get; set; }

    public required string NormalizedUsername { get; set; }

    public required string DisplayName { get; set; }

    public required string PasswordHash { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}