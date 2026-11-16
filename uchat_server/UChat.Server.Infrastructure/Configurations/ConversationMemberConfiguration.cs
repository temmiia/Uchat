using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UChat.Domain.Entities;

namespace UChat.Infrastructure.Configurations;

// Конфигурация сущности ConversationMember для EF Core
public class ConversationMemberConfiguration : IEntityTypeConfiguration<ConversationMember>
{
    public void Configure(EntityTypeBuilder<ConversationMember> builder)
    {
        builder.ToTable("Members");

        builder.HasKey(m => m.MemberId);
        builder.Property(m => m.MemberId)
            .ValueGeneratedNever();

        builder.Property(m => m.ConversationId)
            .IsRequired();

        builder.Property(m => m.AccountId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(m => m.Role)
            .IsRequired();

        builder.Property(m => m.JoinedAtUtc)
            .IsRequired();

        // Пользователь может быть в беседе только один раз
        builder.HasIndex(m => new { m.ConversationId, m.AccountId })
            .IsUnique();

        builder.HasIndex(m => m.AccountId);

        // Каскадное удаление участников при удалении беседы
        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Members)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // При удалении беседы аккаунт пользователя не трогаем (Restrict)
        builder.HasOne(m => m.User)
            .WithMany(u => u.Memberships)
            .HasForeignKey(m => m.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
