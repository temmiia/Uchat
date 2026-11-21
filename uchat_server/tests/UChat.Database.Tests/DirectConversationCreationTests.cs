using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using UChat.Domain.Entities;
using UChat.Domain.Enums;
using UChat.Infrastructure.Data;
using UChat.Server.Controllers;

namespace UChat.Database.Tests;

public class DirectConversationCreationTests
{
    [Fact]
    public async Task CreateDirect_CreatesTwoMemberships_AndReturnsExistingChatOnRepeat()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var dbConnection = connection;
        await using var db = context;
        await AddUsersAsync(db, cancellationToken);

        var created = await CreateController(db, "alice-id").CreateDirect(new("  BOB  "), cancellationToken);
        var createdId = GetConversationId(created, StatusCodes.Status201Created);

        var repeated = await CreateController(db, "bob-id").CreateDirect(new("alice"), cancellationToken);
        Assert.Equal(createdId, GetConversationId(repeated, StatusCodes.Status200OK));
        await AssertSingleDirectChatAsync(db, createdId, cancellationToken);
    }

    [Theory]
    [InlineData("   ", StatusCodes.Status400BadRequest)]
    [InlineData("ALICE", StatusCodes.Status400BadRequest)]
    [InlineData("unknown", StatusCodes.Status404NotFound)]
    public async Task CreateDirect_InvalidTarget_DoesNotWriteAnything(string username, int statusCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var dbConnection = connection;
        await using var db = context;
        await AddUsersAsync(db, cancellationToken);

        var result = await CreateController(db, "alice-id").CreateDirect(new(username), cancellationToken);

        Assert.Equal(statusCode, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        Assert.False(await db.Conversations.AnyAsync(cancellationToken));
        Assert.False(await db.Members.AnyAsync(cancellationToken));
    }

    [Fact]
    public async Task CreateDirect_ConcurrentRequestsInOppositeDirections_ReturnTheSameChat()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("uchat-direct-");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory.FullName, "uchat.db"),
            Pooling = false
        }.ToString();

        try
        {
            await using var verifyDb = OpenContext();
            await DatabaseInitializer.InitializeAsync(verifyDb, cancellationToken);
            await AddUsersAsync(verifyDb, cancellationToken);

            var firstSave = new PauseBeforeSave();
            var secondTransaction = new ObserveTransactionStart();
            var first = RunRequestAsync("alice-id", "bob", firstSave);
            Task<IActionResult> second;
            try
            {
                // Pause after the first lookup and let the second request reach its transaction.
                await firstSave.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                second = RunRequestAsync("bob-id", "alice", secondTransaction);
                await secondTransaction.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            finally
            {
                firstSave.Resume.TrySetResult();
            }

            var results = await Task.WhenAll(first, second);
            var conversationId = GetConversationId(results[0], StatusCodes.Status201Created);
            Assert.Equal(conversationId, GetConversationId(results[1], StatusCodes.Status200OK));
            await AssertSingleDirectChatAsync(verifyDb, conversationId, cancellationToken);
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        AppDbContext OpenContext(params IInterceptor[] interceptors) =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(interceptors)
                .Options);

        Task<IActionResult> RunRequestAsync(string accountId, string username, IInterceptor interceptor) =>
            Task.Run(async () =>
            {
                await using var db = OpenContext(interceptor);
                return await CreateController(db, accountId).CreateDirect(new(username), cancellationToken);
            }, cancellationToken);
    }

    private static ConversationsController CreateController(AppDbContext db, string accountId) =>
        new(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(JwtRegisteredClaimNames.Sub, accountId)], "Test"))
                }
            }
        };

    private static Task AddUsersAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        db.Users.AddRange(
            new User("alice-id", "alice", "Alice", "hash"),
            new User("bob-id", "bob", "Bob", "hash"));
        return db.SaveChangesAsync(cancellationToken);
    }

    private static Guid GetConversationId(IActionResult result, int statusCode)
    {
        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(statusCode, response.StatusCode);
        return Assert.IsType<ConversationsController.ConversationResponse>(response.Value).ConversationId;
    }

    private static async Task AssertSingleDirectChatAsync(
        AppDbContext db,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var conversation = Assert.Single(await db.Conversations
            .AsNoTracking()
            .Include(c => c.Members)
            .ToListAsync(cancellationToken));
        Assert.Equal(conversationId, conversation.ConversationId);
        Assert.Equal(ConversationType.Direct, conversation.Type);
        Assert.Equal(new[] { "alice-id", "bob-id" }, conversation.Members.Select(m => m.AccountId).Order());
    }

    private sealed class PauseBeforeSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            await Resume.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class ObserveTransactionStart : DbTransactionInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
