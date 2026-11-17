
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UChat.Domain.Entities;

namespace UChat.Infrastructure.Configurations;

// EF Core configuration for the User entity
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        // Primary key
        builder.HasKey(u => u.AccountId);

        builder.Property(u => u.AccountId)
            .HasMaxLength(64)
            .IsRequired()
            .ValueGeneratedNever();

        // Prevent AccountId modification after the entity is saved
        builder.Property(u => u.AccountId)
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        // Username with case-insensitive uniqueness for ASCII characters
        builder.Property(u => u.UserName)
            .HasMaxLength(32)
            .IsRequired()
            .UseCollation("NOCASE");

        builder.HasIndex(u => u.UserName)
            .IsUnique();

        // User's display name
        builder.Property(u => u.DisplayName)
            .HasMaxLength(64)
            .IsRequired();

        // Hashed password
        builder.Property(u => u.PasswordHash)
            .HasMaxLength(256)
            .IsRequired();

        // User avatar URL
        builder.Property(u => u.AvatarUrl)
            .HasMaxLength(512);

        // User profile biography
        builder.Property(u => u.Bio)
            .HasMaxLength(256);

        // Account creation timestamp
        builder.Property(u => u.CreatedAtUtc)
            .IsRequired();

        // Last account update timestamp
        builder.Property(u => u.UpdatedAtUtc);
    }
}
