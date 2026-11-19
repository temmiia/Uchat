using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UChat.Domain.Entities;

namespace UChat.Infrastructure.Configurations;

// EF Core configuration for Message
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

        builder.Property(m => m.ServerReceivedAtUtc);

        builder.Property(m => m.EditedAtUtc);

        builder.Property(m => m.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(m => m.ClientMessageId)
            .HasMaxLength(64);

        // Index for fast chronological message loading & pagination within a chat
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAtUtc });

        // Unique index for idempotency: scoped to Conversation + Sender + ClientMessageId
        builder.HasIndex(m => new { m.ConversationId, m.SenderAccountId, m.ClientMessageId })
            .IsUnique()
            .HasFilter("[ClientMessageId] IS NOT NULL");

        // Cascade delete messages when conversation is removed
        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Do not delete user when messages are deleted (Restrict)
        builder.HasOne(m => m.Sender)
            .WithMany(u => u.Messages)
            .HasForeignKey(m => m.SenderAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
