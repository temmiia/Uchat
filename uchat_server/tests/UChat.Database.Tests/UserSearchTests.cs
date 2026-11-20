using UChat.Application.Users;
using UChat.Domain.Entities;
using UChat.Infrastructure.Repositories;

namespace UChat.Database.Tests;

public class UserSearchTests
{
    [Fact]
    public async Task SearchByUsernameAsync_Finds_Users_Matching_Query_Case_Insensitive()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user1 = new User("acc_1", "alex_dev", "Alex Developer", "hash1");
        var user2 = new User("acc_2", "alexander_p", "Alexander P", "hash2");
        var user3 = new User("acc_3", "bob_ross", "Bob Ross", "hash3");

        context.Users.AddRange(user1, user2, user3);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfUserRepository(context);
        var searchService = new SearchUsersService(repo);

        // Search for "ALEX" in uppercase - should match both alex_dev and alexander_p
        var results = await searchService.SearchAsync("ALEX", limit: 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.UserName == "alex_dev");
        Assert.Contains(results, r => r.UserName == "alexander_p");
        Assert.DoesNotContain(results, r => r.UserName == "bob_ross");
    }

    [Fact]
    public async Task SearchByUsernameAsync_Handles_At_Prefix_Correctly()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user = new User("acc_4", "charlie", "Charlie", "hash");
        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfUserRepository(context);
        var searchService = new SearchUsersService(repo);

        // Search with "@charlie" prefix should resolve to "charlie"
        var results = await searchService.SearchAsync("@charlie", limit: 10, TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("charlie", results[0].UserName);
        Assert.Equal("acc_4", results[0].AccountId);
    }

    [Fact]
    public async Task SearchByUsernameAsync_Respects_Limit()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        for (int i = 1; i <= 10; i++)
        {
            context.Users.Add(new User($"acc_{i}", $"testuser_{i:D2}", $"User {i}", "hash"));
        }
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfUserRepository(context);
        var searchService = new SearchUsersService(repo);

        var results = await searchService.SearchAsync("testuser", limit: 3, TestContext.Current.CancellationToken);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task SearchByUsernameAsync_Returns_Empty_For_Empty_Or_Whitespace_Query()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var repo = new EfUserRepository(context);
        var searchService = new SearchUsersService(repo);

        var resultsEmpty = await searchService.SearchAsync("", limit: 10, TestContext.Current.CancellationToken);
        var resultsSpaces = await searchService.SearchAsync("   ", limit: 10, TestContext.Current.CancellationToken);
        var resultsAtOnly = await searchService.SearchAsync("@", limit: 10, TestContext.Current.CancellationToken);

        Assert.Empty(resultsEmpty);
        Assert.Empty(resultsSpaces);
        Assert.Empty(resultsAtOnly);
    }
}
