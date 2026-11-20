using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UChat.Application.Abstractions;
using UChat.Application.Auth;
using UChat.Database.Tests;
using UChat.Domain.Entities;
using UChat.Infrastructure.Data;
using UChat.Infrastructure.Repositories;

namespace UChat.Application.Tests;

public class UserRegistrationTests
{
    private static (RegisterUserService Service, AppDbContext Context, SqliteConnection Connection, PasswordHasher<User> Hasher) CreateSut()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        IUserRepository userRepository = new EfUserRepository(context);
        var passwordHasher = new PasswordHasher<User>();
        var service = new RegisterUserService(userRepository, passwordHasher);

        return (service, context, connection, passwordHasher);
    }

    #region Successful Registration & Normalization

    [Fact]
    public async Task RegisterAsync_ValidRequest_PersistsUserWithHashedPasswordAndGeneratedAccountId()
    {
        // Arrange
        var (service, context, connection, passwordHasher) = CreateSut();
        await using var _ = connection;
        await using var __ = context;

        var request = new RegisterRequest(
            Username: "Cat",
            DisplayName: "Sobaka",
            Password: "StrongPassword123!"
        );

        // Act
        var createdUser = await service.RegisterAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(createdUser);
        Assert.Equal(32, createdUser.AccountId.Length); // Guid.NewGuid().ToString("N")
        Assert.Equal("Cat", createdUser.UserName); // Оригінальний регістр зберігається
        Assert.Equal("Sobaka", createdUser.DisplayName);
        Assert.NotEqual("StrongPassword123!", createdUser.PasswordHash);
        Assert.NotEqual("PENDING_HASH", createdUser.PasswordHash);

        var savedUser = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.AccountId == createdUser.AccountId, TestContext.Current.CancellationToken);

        Assert.NotNull(savedUser);
        Assert.Equal("Cat", savedUser.UserName);
        Assert.Equal("Sobaka", savedUser.DisplayName);

        var verificationResult = passwordHasher.VerifyHashedPassword(
            savedUser,
            savedUser.PasswordHash,
            "StrongPassword123!");
        Assert.Equal(PasswordVerificationResult.Success, verificationResult);
    }

    [Fact]
    public async Task RegisterAsync_TrimsLeadingAndTrailingWhitespace_FromUsernameAndDisplayName()
    {
        // Arrange
        var (service, context, connection, _) = CreateSut();
        await using var _ = connection;
        await using var __ = context;

        var request = new RegisterRequest(
            Username: "   alex_dev   ",
            DisplayName: "   Alexandr   ",
            Password: "Password123!"
        );

        // Act
        var createdUser = await service.RegisterAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("alex_dev", createdUser.UserName);
        Assert.Equal("Alexandr", createdUser.DisplayName);

        var savedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("alex_dev", savedUser.UserName);
        Assert.Equal("Alexandr", savedUser.DisplayName);
    }

    [Fact]
    public async Task RegisterAsync_MultipleUsers_GeneratesDistinctAccountIds()
    {
        // Arrange
        var (service, context, connection, _) = CreateSut();
        await using var _ = connection;
        await using var __ = context;

        // Act
        var user1 = await service.RegisterAsync(
            new RegisterRequest("user_one", "User One", "Password123!"),
            TestContext.Current.CancellationToken);

        var user2 = await service.RegisterAsync(
            new RegisterRequest("user_two", "User Two", "Password123!"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(user1.AccountId, user2.AccountId);
        Assert.Equal(2, await context.Users.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("abc")] // Мінімально допустима довжина: 3 символи
    [InlineData("a2345678901234567890123456789012")] // Максимально допустима довжина: 32 символи
    public async Task RegisterAsync_BoundaryUsernameLengths_Succeeds(string validUsername)
    {
        // Arrange
        var (service, context, connection, _) = CreateSut();
        await using var _ = connection;
        await using var __ = context;

        var request = new RegisterRequest(validUsername, "Valid User", "Password123!");

        // Act
        var user = await service.RegisterAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(validUsername, user.UserName);
        Assert.Equal(1, await context.Users.CountAsync(TestContext.Current.CancellationToken));
    }

    #endregion

    #region Unique Username & Concurrency

    [Theory]
    [InlineData("alexandr", "alexandr")]     // Точний дублікат
    [InlineData("alexandr", "Alexandr")]     // Інший регістр (перша велика)
    [InlineData("alexandr", "ALEXANDR")]     // Верхній регістр
    [InlineData("Alexandr", "aLeXaNdR")]     // Змішаний регістр
    [InlineData("alexandr", "  alexandr  ")] // Дублікат із пробілами по краях
    [InlineData("alexandr", "  ALEXANDR  ")] // Дублікат із пробілами та іншим регістром
    public async Task RegisterAsync_DuplicateUsername_ThrowsInvalidOperationException(
        string existingUsername,
        string duplicateUsername)
    {
        // Arrange
        var (service, context, connection, _) = CreateSut();
        await using var _ = connection;
        await using var __ = context;

        await service.RegisterAsync(
            new RegisterRequest(existingUsername, "First User", "Password123!"),
            TestContext.Current.CancellationToken);

        var duplicateRequest = new RegisterRequest(
            duplicateUsername,
            "Second User",
            "AnotherPassword123!");

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RegisterAsync(duplicateRequest, TestContext.Current.CancellationToken));

        // Assert (AuthController очікує саме це повідомлення для повернення 409 Conflict)
        Assert.Equal("Username is already taken.", exception.Message);
        Assert.Equal(1, await context.Users.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterAsync_ConcurrentRequestsWithSameUsername_CreatesOnlyOneUser()
    {
        // Arrange: використовуємо спільну файлову SQLite БД, щоб кожен потік мав власний DbContext
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"uchat_reg_race_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={tempDbPath}";

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .Options;

            await using (var initContext = new AppDbContext(options))
            {
                await DatabaseInitializer.InitializeAsync(initContext, TestContext.Current.CancellationToken);
            }

            async Task<User?> TryRegisterAsync(string username, string displayName)
            {
                await using var ctx = new AppDbContext(options);
                var svc = new RegisterUserService(new EfUserRepository(ctx), new PasswordHasher<User>());
                try
                {
                    return await svc.RegisterAsync(
                        new RegisterRequest(username, displayName, "Password123!"),
                        TestContext.Current.CancellationToken);
                }
                catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
                {
                    return null;
                }
            }

            // Act: два одночасних запити на один нікнейм у різному регістрі
            var results = await Task.WhenAll(
                TryRegisterAsync("race_nick", "User One"),
                TryRegisterAsync("RACE_NICK", "User Two"));

            // Assert: завдяки індексу UNIQUE + COLLATE NOCASE в базі зберігається рівно 1 користувач
            Assert.Single(results, r => r is not null);
            Assert.Single(results, r => r is null);

            await using var verifyContext = new AppDbContext(options);
            Assert.Equal(1, await verifyContext.Users.CountAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var ext in new[] { "", "-wal", "-shm" })
            {
                var file = tempDbPath + ext;
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }

    #endregion

    #region Input Validation

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RegisterAsync_MissingOrWhitespaceUsername_ThrowsArgumentException(string? invalidUsername)
    {
        // Arrange
        var (service, context, connection, _) = CreateSut();
        await using var _ = connection;
        await using var __ = context;

        var request = new RegisterRequest(invalidUsername!, "Valid Name", "Password123!");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.RegisterAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal("Username is required.", exception.Message);
        Assert.Equal(0, await context.Users.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("ab")]                                  // 2 символи (< 3)
    [InlineData("  ab  ")]                              // 2 символи після Trim()
    [InlineData("a23456789012345678901234567890123")]   // 33 символи (> 32)
    public async Task RegisterAsync_UsernameLengthOutOfRange_ThrowsArgumentException(string invalidUsername)
    {
        // Arrange
        var (service, context, connection, _) = CreateSut();
        await using var _ = connection;
        await using var __ = context;

        var request = new RegisterRequest(invalidUsername, "Valid Name", "Password123!");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.RegisterAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal("Username must contain between 3 and 32 characters.", exception.Message);
        Assert.Equal(0, await context.Users.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RegisterAsync_MissingOrWhitespaceDisplayName_ThrowsArgumentException(string? invalidDisplayName)
    {
        // Arrange
        var (service, context, connection, _) = CreateSut();
        await using var _ = connection;
        await using var __ = context;

        var request = new RegisterRequest("valid_user", invalidDisplayName!, "Password123!");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.RegisterAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal("Display name is required.", exception.Message);
        Assert.Equal(0, await context.Users.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RegisterAsync_MissingOrWhitespacePassword_ThrowsArgumentException(string? invalidPassword)
    {
        // Arrange
        var (service, context, connection, _) = CreateSut();
        await using var _ = connection;
        await using var __ = context;

        var request = new RegisterRequest("valid_user", "Valid Name", invalidPassword!);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.RegisterAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal("Password is required.", exception.Message);
        Assert.Equal(0, await context.Users.CountAsync(TestContext.Current.CancellationToken));
    }

    #endregion
}
