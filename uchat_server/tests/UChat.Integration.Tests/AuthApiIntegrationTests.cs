using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UChat.Application.Auth;
using UChat.Integration.Tests.Infrastructure;

namespace UChat.Integration.Tests;

public class AuthApiIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Register_ValidRequest_PersistsUserInDatabase_AndReturns201Created()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var regRequest = new RegisterRequest("john_doe", "John Doe", "SecurePassword123!");
        var response = await client.PostAsJsonAsync("/api/auth/register", regRequest, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var regResponse = await response.Content.ReadFromJsonAsync<RegisterResponse>(JsonOptions, ct);
        Assert.NotNull(regResponse);
        Assert.Equal("john_doe", regResponse.Username);
        Assert.Equal("John Doe", regResponse.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(regResponse.AccountId));

        // Directly verify the database record
        using var db = factory.CreateDbContext();
        var userInDb = await db.Users.SingleOrDefaultAsync(u => u.AccountId == regResponse.AccountId, ct);
        Assert.NotNull(userInDb);
        Assert.Equal("john_doe", userInDb.UserName);
        Assert.Equal("John Doe", userInDb.DisplayName);
        // Password must be hashed, never plain text
        Assert.NotEqual("SecurePassword123!", userInDb.PasswordHash);
    }

    [Fact]
    public async Task Register_DuplicateUsername_Returns409Conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var reg1 = new RegisterRequest("sam_unique", "Sam 1", "Password123!");
        var resp1 = await client.PostAsJsonAsync("/api/auth/register", reg1, ct);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        // Attempt to register with same username (different case)
        var reg2 = new RegisterRequest("SAM_UNIQUE", "Sam 2", "Password456!");
        var resp2 = await client.PostAsJsonAsync("/api/auth/register", reg2, ct);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
    }

    [Theory]
    [InlineData("", "Display", "Password123!")]
    [InlineData("ab", "Display", "Password123!")] // Too short (< 3)
    [InlineData("valid_user", "", "Password123!")] // Empty display name
    [InlineData("valid_user", "Display", "")] // Empty password
    public async Task Register_InvalidInput_Returns400BadRequest(string username, string displayName, string password)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, displayName, password), ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsJwtToken()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("login_user", "Login User", "Password123!"), ct);

        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("login_user", "Password123!"), ct);
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

        var result = await loginResp.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, ct);
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.Equal("Bearer", result.TokenType);
    }

    [Fact]
    public async Task Login_InvalidPassword_Returns401Unauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("wrong_pass_user", "User", "RightPassword123!"), ct);

        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("wrong_pass_user", "WrongPassword!"), ct);
        Assert.Equal(HttpStatusCode.Unauthorized, loginResp.StatusCode);
    }

    [Fact]
    public async Task Me_WithValidJwtToken_ReturnsUserClaims()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var (accountId, token) = await client.RegisterAndLoginAsync("me_tester", "Me Tester", "Password123!", ct);

        using var authedClient = factory.CreateAuthorizedClient(token);
        var meResponse = await authedClient.GetAsync("/api/auth/me", ct);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var content = await meResponse.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(content);
        Assert.Equal(accountId, doc.RootElement.GetProperty("accountId").GetString());
        Assert.Equal("me_tester", doc.RootElement.GetProperty("username").GetString());
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401Unauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var meResponse = await client.GetAsync("/api/auth/me", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }
}
