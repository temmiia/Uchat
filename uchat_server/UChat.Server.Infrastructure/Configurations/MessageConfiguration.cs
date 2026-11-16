using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UChat.Domain.Entities;

namespace UChat.Infrastructure.Configurations;

// Конфигурация сущности Message для EF Core
public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");

        builder.HasKey(m => m.MessageId);
        builder.Property(m => m.MessageId)
            .ValueGeneratedNever();

        builder.Property(m => m.ConversationId)
            .IsRequired();

        builder.Property(m => m.SenderAccountId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(m => m.Content)
            .HasMaxLength(4096)
            .IsRequired();

        builder.Property(m => m.CreatedAtUtc)
            .IsRequired();

        builder.Property(m => m.EditedAtUtc);

        builder.Property(m => m.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(m => m.ClientMessageId)
            .HasMaxLength(64);

        // Индекс для быстрой загрузки истории чата по времени
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAtUtc });

        // Индекс для идемпотентности: (ConversationId, SenderAccountId, ClientMessageId)
        builder.HasIndex(m => new { m.ConversationId, m.SenderAccountId, m.ClientMessageId })
            .IsUnique()
            .HasFilter("[ClientMessageId] IS NOT NULL");

        // Каскадное удаление сообщений при удалении беседы
        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Пользователя при удалении сообщений не трогаем (Restrict)
        builder.HasOne(m => m.Sender)
            .WithMany(u => u.Messages)
            .HasForeignKey(m => m.SenderAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
