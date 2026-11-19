using Microsoft.EntityFrameworkCore;
using UChat.Application.Abstractions;
using UChat.Domain.Entities;
using UChat.Infrastructure.Data;

namespace UChat.Infrastructure.Repositories;

public sealed class EfUserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public EfUserRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<bool> ExistsByUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken = default)
    {
        return _context.Users.AnyAsync(
            user => user.UserName.ToUpper() == normalizedUsername,
            cancellationToken);
    }

    public async Task CreateAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<User?> GetByUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken = default)
    {
        return _context.Users.FirstOrDefaultAsync(
            user => user.UserName.ToUpper() == normalizedUsername,
            cancellationToken);
    }

    public async Task<IReadOnlyList<User>> SearchByUsernameAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<User>();
        }

        var normalized = query.ToUpper();

        return await _context.Users
            .AsNoTracking()
            .Where(user => user.UserName.ToUpper().Contains(normalized))
            .OrderBy(user => user.UserName)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
