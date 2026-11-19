using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UChat.Domain.Entities;

namespace UChat.Infrastructure.Configurations;

// EF Core configuration for ConversationMember (Members table)
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

        // A user can only join a conversation once
        builder.HasIndex(m => new { m.ConversationId, m.AccountId })
            .IsUnique();

        builder.HasIndex(m => m.AccountId);

        // Cascade delete membership records when conversation is removed
        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Members)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Do not delete user account when conversation is removed (Restrict)
        builder.HasOne(m => m.User)
            .WithMany(u => u.Memberships)
            .HasForeignKey(m => m.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
