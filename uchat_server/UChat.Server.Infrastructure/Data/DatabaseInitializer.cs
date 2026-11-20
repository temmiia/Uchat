using Microsoft.EntityFrameworkCore;

namespace UChat.Infrastructure.Data;

// Automated database schema initialization and SQLite optimization
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Ensure database file and schema exist
        await context.Database.EnsureCreatedAsync(cancellationToken);

        // Optimize SQLite pragmas for high concurrency and referential integrity
        if (context.Database.IsSqlite())
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL;", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;", cancellationToken);
        }
    }
}
