using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UChat.Domain.Entities;

namespace UChat.Infrastructure.Configurations;

// Конфигурация сущности Conversation для EF Core
public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");

        builder.HasKey(c => c.ConversationId);
        builder.Property(c => c.ConversationId)
            .ValueGeneratedNever();

        builder.Property(c => c.Type)
            .IsRequired();

        builder.Property(c => c.Title)
            .HasMaxLength(128);

        builder.Property(c => c.CreatedAtUtc)
            .IsRequired();

        builder.Property(c => c.UpdatedAtUtc);
    }
}
