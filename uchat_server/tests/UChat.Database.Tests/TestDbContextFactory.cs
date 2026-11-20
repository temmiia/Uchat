using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UChat.Infrastructure.Data;

namespace UChat.Database.Tests;

// Factory creating isolated in-memory SQLite database contexts for tests
public static class TestDbContextFactory
{
    public static (AppDbContext Context, SqliteConnection Connection) CreateInMemoryDbContext()
    {
        // Keep the SQLite in-memory connection open, otherwise the database will be destroyed
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .EnableSensitiveDataLogging()
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();

        return (context, connection);
    }
}
