using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UChat.Domain.Entities;

namespace UChat.Infrastructure.Configurations;

// Конфигурация сущности User для EF Core
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        // Первичный ключ
        builder.HasKey(u => u.AccountId);

        builder.Property(u => u.AccountId)
            .HasMaxLength(64)
            .IsRequired()
            .ValueGeneratedNever();

        // 2-й уровень защиты: запрещаем изменять AccountId на уровне метаданных EF Core
        builder.Property(u => u.AccountId)
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        // Уникальный индекс для логина и поиска
        builder.Property(u => u.UserName)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(u => u.UserName)
            .IsUnique();

        builder.Property(u => u.DisplayName)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(u => u.AvatarUrl)
            .HasMaxLength(512);

        builder.Property(u => u.Bio)
            .HasMaxLength(256);

        builder.Property(u => u.CreatedAtUtc)
            .IsRequired();

        builder.Property(u => u.UpdatedAtUtc);
    }
}
