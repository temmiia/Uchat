using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UChat.Application.Users;
using UChat.Integration.Tests.Infrastructure;

namespace UChat.Integration.Tests;

public class UsersApiIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SearchUsers_WithPrefix_ReturnsMatchingUsers_ExcludingRequester()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (_, tokenA) = await client.RegisterAndLoginAsync("alice_prime", "Alice P", "Password123!", ct);
        await client.RegisterAndLoginAsync("alex_smith", "Alex S", "Password123!", ct);
        await client.RegisterAndLoginAsync("alexander_great", "Alexander G", "Password123!", ct);
        await client.RegisterAndLoginAsync("bob_jones", "Bob J", "Password123!", ct);

        using var authedClient = factory.CreateAuthorizedClient(tokenA);

        // Search with query "alex"
        var response = await authedClient.GetAsync("/api/users/search?query=alex", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = await response.Content.ReadFromJsonAsync<IReadOnlyList<UserSearchResult>>(JsonOptions, ct);
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.UserName == "alex_smith");
        Assert.Contains(results, r => r.UserName == "alexander_great");
        Assert.DoesNotContain(results, r => r.UserName == "bob_jones");
    }

    [Fact]
    public async Task SearchUsers_StripsLeadingAtSymbol()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (_, token) = await client.RegisterAndLoginAsync("search_user_1", "User 1", "Password123!", ct);
        await client.RegisterAndLoginAsync("target_dev", "Target Dev", "Password123!", ct);

        using var authedClient = factory.CreateAuthorizedClient(token);

        var response = await authedClient.GetAsync("/api/users/search?query=@target", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = await response.Content.ReadFromJsonAsync<IReadOnlyList<UserSearchResult>>(JsonOptions, ct);
        Assert.NotNull(results);
        Assert.Single(results);
        Assert.Equal("target_dev", results[0].UserName);
    }

    [Fact]
    public async Task SearchUsers_Unauthenticated_Returns401Unauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/users/search?query=test", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
