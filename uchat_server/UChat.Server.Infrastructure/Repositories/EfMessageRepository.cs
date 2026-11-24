using Microsoft.EntityFrameworkCore;
using UChat.Application.Abstractions;
using UChat.Domain.Entities;
using UChat.Infrastructure.Data;

namespace UChat.Infrastructure.Repositories;

public sealed class EfMessageRepository : IMessageRepository
{
    private readonly AppDbContext _context;

    public EfMessageRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<bool> CreateAsync(
        Message message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _context.Messages.Add(message);

            await _context.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException)
            when (!string.IsNullOrWhiteSpace(message.ClientMessageId))
        {
            _context.Entry(message).State = EntityState.Detached;

            return false;
        }
    }

    public async Task<IReadOnlyList<Message>>
        GetByConversationIdAsync(
            Guid conversationId,
            int limit = 50,
            DateTime? beforeUtc = null,
            DateTime? afterUtc = null,
            CancellationToken cancellationToken = default)
    {
        var query = _context.Messages
            .AsNoTracking()
            .Include(m => m.Sender)
            .Where(m =>
                m.ConversationId == conversationId
                && !m.IsDeleted);

        if (afterUtc.HasValue)
        {
            return await query
                .Where(m => m.CreatedAtUtc > afterUtc.Value)
                .OrderBy(m => m.CreatedAtUtc)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        if (beforeUtc.HasValue)
        {
            query = query.Where(
                m => m.CreatedAtUtc < beforeUtc.Value);
        }

        var items = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        items.Reverse();

        return items;
    }

    public Task<Message?> GetByIdAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        return _context.Messages
            .AsNoTracking()
            .Include(m => m.Sender)
            .FirstOrDefaultAsync(
                m => m.MessageId == messageId,
                cancellationToken);
    }

    public Task<Message?> GetByClientMessageIdAsync(
        Guid conversationId,
        string senderAccountId,
        string clientMessageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientMessageId))
        {
            return Task.FromResult<Message?>(null);
        }

        return _context.Messages
            .AsNoTracking()
            .Include(m => m.Sender)
            .FirstOrDefaultAsync(
                m => m.ConversationId == conversationId
                     && m.SenderAccountId == senderAccountId
                     && m.ClientMessageId == clientMessageId,
                cancellationToken);
    }

    public async Task UpdateAsync(
        Message message,
        CancellationToken cancellationToken = default)
    {
        var entry = _context.Entry(message);

        entry.State = EntityState.Unchanged;

        entry.Property(m => m.Content).IsModified = true;
        entry.Property(m => m.EditedAtUtc).IsModified = true;
        entry.Property(m => m.IsDeleted).IsModified = true;

        await _context.SaveChangesAsync(cancellationToken);
    }
}
