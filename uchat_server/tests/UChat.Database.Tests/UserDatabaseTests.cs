using Microsoft.EntityFrameworkCore;
using UChat.Domain.Entities;

namespace UChat.Database.Tests;

public class UserDatabaseTests
{
    [Fact]
    public async Task Can_Create_And_Persist_User_With_Required_Fields()
    {
        // Проверяем создание пользователя со всеми обязательными и опциональными полями
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user = new User(
            accountId: "acc_usr_12345",
            userName: "alexandr",
            displayName: "Alexandr Petrenko",
            passwordHash: "AQAAAAIAAYagAAAAEN..."
        )
        {
            AvatarUrl = "avatars/alexandr.png",
            Bio = "Software Engineer"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var retrieved = await context.Users.FirstOrDefaultAsync(u => u.AccountId == "acc_usr_12345", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(retrieved);
        Assert.Equal("acc_usr_12345", retrieved.AccountId);
        Assert.Equal("alexandr", retrieved.UserName);
        Assert.Equal("Alexandr Petrenko", retrieved.DisplayName);
        Assert.Equal("avatars/alexandr.png", retrieved.AvatarUrl);
        Assert.Equal("Software Engineer", retrieved.Bio);
    }

    [Fact]
    public async Task AccountId_Is_Immutable_And_Throws_On_Modification_Attempt()
    {
        // Проверяем неизменность AccountId (требование ТЗ)
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user = new User("acc_permanent_id_1", "john_doe", "John Doe", "hashed_pwd");
        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Попытка изменить AccountId у сохраненного пользователя должна приводить к ошибке
        var entry = context.Entry(user);
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            entry.Property(u => u.AccountId).CurrentValue = "acc_tampered_id";
        });

        Assert.Contains("cannot be modified", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Duplicate_UserName_Throws_DbUpdateException()
    {
        // Проверяем уникальность UserName
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user1 = new User("acc_001", "same_username", "First User", "hash1");
        var user2 = new User("acc_002", "same_username", "Second User", "hash2");

        context.Users.Add(user1);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Users.Add(user2);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisplayName_Can_Be_Updated_Successfully()
    {
        // Проверяем, что DisplayName можно обновлять в отличие от AccountId
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user = new User("acc_display_test", "tester", "Old Name", "hash");
        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        user.DisplayName = "New Name";
        user.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var updated = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.AccountId == "acc_display_test", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal("New Name", updated.DisplayName);
    }
}
