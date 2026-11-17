using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UChat.Infrastructure.Data;

namespace UChat.Database.Tests;

public class DatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_Configures_Database_And_Enables_Pragmas()
    {
        // Проверяем создание базы, таблиц и включение режима WAL
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"uchat_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={tempDbPath}";

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .Options;

            await using (var context = new AppDbContext(options))
            {
                await DatabaseInitializer.InitializeAsync(context, TestContext.Current.CancellationToken);

                // Таблицы должны быть созданы
                Assert.False(await context.Users.AnyAsync(cancellationToken: TestContext.Current.CancellationToken));
                Assert.False(await context.Conversations.AnyAsync(cancellationToken: TestContext.Current.CancellationToken));
                Assert.False(await context.Members.AnyAsync(cancellationToken: TestContext.Current.CancellationToken));
                Assert.False(await context.Messages.AnyAsync(cancellationToken: TestContext.Current.CancellationToken));

                // Проверяем, что включен journal_mode = WAL
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText = "PRAGMA journal_mode;";
                await context.Database.OpenConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
                var result = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
                Assert.Equal("wal", result?.ToLowerInvariant());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tempDbPath))
            {
                File.Delete(tempDbPath);
            }
            var walFile = tempDbPath + "-wal";
            if (File.Exists(walFile))
            {
                File.Delete(walFile);
            }
            var shmFile = tempDbPath + "-shm";
            if (File.Exists(shmFile))
            {
                File.Delete(shmFile);
            }
        }
    }
}
