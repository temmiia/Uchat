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
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(message.ClientMessageId))
        {
            // Detach duplicate entity so context state remains valid
            _context.Entry(message).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<Message>> GetByConversationIdAsync(
        Guid conversationId,
        int limit = 50,
        DateTime? beforeUtc = null,
        DateTime? afterUtc = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && !m.IsDeleted);

        // If afterUtc is provided (reconnect catch-up): fetch new messages since that timestamp
        if (afterUtc.HasValue)
        {
            return await query
                .Where(m => m.CreatedAtUtc > afterUtc.Value)
                .OrderBy(m => m.CreatedAtUtc)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        // If beforeUtc is provided (history pagination): fetch older messages
        if (beforeUtc.HasValue)
        {
            query = query.Where(m => m.CreatedAtUtc < beforeUtc.Value);
        }

        // Fetch latest messages and reverse back to chronological order for chat rendering
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
        var local = _context.Messages.Local.FirstOrDefault(m => m.MessageId == messageId);
        if (local != null)
        {
            return Task.FromResult<Message?>(local);
        }

        return _context.Messages
            .FirstOrDefaultAsync(m => m.MessageId == messageId, cancellationToken);
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
            .FirstOrDefaultAsync(
                m => m.ConversationId == conversationId &&
                     m.SenderAccountId == senderAccountId &&
                     m.ClientMessageId == clientMessageId,
                cancellationToken);
    }

    public async Task UpdateAsync(
        Message message,
        CancellationToken cancellationToken = default)
    {
        var local = _context.Messages.Local.FirstOrDefault(m => m.MessageId == message.MessageId);
        if (local != null && !ReferenceEquals(local, message))
        {
            _context.Entry(local).State = EntityState.Detached;
            _context.Messages.Update(message);
        }
        else if (local == null)
        {
            _context.Messages.Update(message);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
