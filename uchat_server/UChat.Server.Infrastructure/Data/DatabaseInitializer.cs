using Microsoft.EntityFrameworkCore;

namespace UChat.Infrastructure.Data;

// Инициализация базы данных и оптимизация SQLite при старте
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Создаем файл БД и таблицы, если их еще нет
        await context.Database.EnsureCreatedAsync(cancellationToken);

        // Оптимизация SQLite: включаем режим WAL и проверку внешних ключей
        if (context.Database.IsSqlite())
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL;", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;", cancellationToken);
        }
    }
}
