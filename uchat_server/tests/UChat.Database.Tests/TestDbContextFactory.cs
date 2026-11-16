using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UChat.Infrastructure.Data;

namespace UChat.Database.Tests;

// Фабрика для создания тестовой in-memory базы SQLite
public static class TestDbContextFactory
{
    public static (AppDbContext Context, SqliteConnection Connection) CreateInMemoryDbContext()
    {
        // Держим соединение открытым, иначе in-memory база удалится
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
