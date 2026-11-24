using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UChat.Domain.Entities;
using UChat.Domain.Enums;
using UChat.Infrastructure.Data;
using UChat.Server.Controllers;
using UChat.Server.Hubs;
using UChat.Server.Services;

namespace UChat.Database.Tests;

public class GroupConversationCreationTests
{
    [Fact]
    public async Task CreateGroup_CreatesOwnerAndMembers()
    {
        var ct = TestContext.Current.CancellationToken;

        var (db, connection) =
            TestDbContextFactory.CreateInMemoryDbContext();

        await using var connectionScope = connection;
        await using var dbScope = db;

        using var services = CreateServices();

        await AddUsersAsync(db, ct);

        var controller = CreateController(
            db,
            "acc_alice",
            services);

        var result = await controller.CreateGroup(
            new ConversationsController.CreateGroupRequest(
                "Developers",
                new[] { "bob", "charlie" }),
            ct);

        var created = Assert.IsType<ObjectResult>(result);

        Assert.Equal(
            StatusCodes.Status201Created,
            created.StatusCode);

        var response = Assert.IsType<
            ConversationsController.ConversationResponse>(
                created.Value);

        Assert.Equal("Group", response.Type);
        Assert.Equal("Developers", response.DisplayName);
        Assert.Equal("Owner", response.MyRole);

        var conversation = await db.Conversations
            .AsNoTracking()
            .Include(c => c.Members)
            .SingleAsync(ct);

        Assert.Equal(
            ConversationType.Group,
            conversation.Type);

        Assert.Equal(
            response.ConversationId,
            conversation.ConversationId);

        Assert.Equal(3, conversation.Members.Count);

        var owner = Assert.Single(
    conversation.Members,
    m => m.Role == MemberRole.Owner);

        Assert.Equal("acc_alice", owner.AccountId);

        Assert.Contains(
            conversation.Members,
            m => m.AccountId == "acc_bob"
                 && m.Role == MemberRole.Member);

        Assert.Contains(
            conversation.Members,
            m => m.AccountId == "acc_charlie"
                 && m.Role == MemberRole.Member);
    }

    [Fact]
    public async Task CreateGroup_IgnoresDuplicateMembers()
    {
        var ct = TestContext.Current.CancellationToken;

        var (db, connection) =
            TestDbContextFactory.CreateInMemoryDbContext();

        await using var connectionScope = connection;
        await using var dbScope = db;

        using var services = CreateServices();

        await AddUsersAsync(db, ct);

        var controller = CreateController(
            db,
            "acc_alice",
            services);

        var result = await controller.CreateGroup(
            new ConversationsController.CreateGroupRequest(
                "Test group",
                new[]
                {
                    "bob",
                    "BOB",
                    " bob ",
                    "alice",
                    "ALICE"
                }),
            ct);

        var created = Assert.IsType<ObjectResult>(result);

        Assert.Equal(
            StatusCodes.Status201Created,
            created.StatusCode);

        var members = await db.Members
            .AsNoTracking()
            .ToListAsync(ct);

        Assert.Equal(2, members.Count);

        Assert.Single(
    members,
    m => m.Role == MemberRole.Owner);

        Assert.Single(
            members,
            m => m.AccountId == "acc_bob");

        Assert.Single(
            members,
            m => m.AccountId == "acc_alice");
    }

    [Fact]
    public async Task CreateGroup_UnknownUser_DoesNotSaveGroup()
    {
        var ct = TestContext.Current.CancellationToken;

        var (db, connection) =
            TestDbContextFactory.CreateInMemoryDbContext();

        await using var connectionScope = connection;
        await using var dbScope = db;

        using var services = CreateServices();

        await AddUsersAsync(db, ct);

        var controller = CreateController(
            db,
            "acc_alice",
            services);

        var result = await controller.CreateGroup(
            new ConversationsController.CreateGroupRequest(
                "Invalid group",
                new[] { "bob", "unknown_user" }),
            ct);

        Assert.IsType<NotFoundObjectResult>(result);

        Assert.False(
            await db.Conversations.AnyAsync(ct));

        Assert.False(
            await db.Members.AnyAsync(ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateGroup_EmptyTitle_IsRejected(
        string title)
    {
        var ct = TestContext.Current.CancellationToken;

        var (db, connection) =
            TestDbContextFactory.CreateInMemoryDbContext();

        await using var connectionScope = connection;
        await using var dbScope = db;

        using var services = CreateServices();

        await AddUsersAsync(db, ct);

        var controller = CreateController(
            db,
            "acc_alice",
            services);

        var result = await controller.CreateGroup(
            new ConversationsController.CreateGroupRequest(
                title,
                new[] { "bob" }),
            ct);

        Assert.IsType<BadRequestObjectResult>(result);

        Assert.False(
            await db.Conversations.AnyAsync(ct));

        Assert.False(
            await db.Members.AnyAsync(ct));
    }

    [Fact]
    public async Task CreateGroup_WithoutMembers_IsRejected()
    {
        var ct = TestContext.Current.CancellationToken;

        var (db, connection) =
            TestDbContextFactory.CreateInMemoryDbContext();

        await using var connectionScope = connection;
        await using var dbScope = db;

        using var services = CreateServices();

        await AddUsersAsync(db, ct);

        var controller = CreateController(
            db,
            "acc_alice",
            services);

        var result = await controller.CreateGroup(
            new ConversationsController.CreateGroupRequest(
                "Empty group",
                Array.Empty<string>()),
            ct);

        Assert.IsType<BadRequestObjectResult>(result);

        Assert.False(
            await db.Conversations.AnyAsync(ct));

        Assert.False(
            await db.Members.AnyAsync(ct));
    }

    [Fact]
    public async Task GetConversation_ReturnsCorrectRole()
    {
        var ct = TestContext.Current.CancellationToken;

        var (db, connection) =
            TestDbContextFactory.CreateInMemoryDbContext();

        await using var connectionScope = connection;
        await using var dbScope = db;

        using var services = CreateServices();

        await AddUsersAsync(db, ct);

        var ownerController = CreateController(
            db,
            "acc_alice",
            services);

        var result = await ownerController.CreateGroup(
            new ConversationsController.CreateGroupRequest(
                "Developers",
                new[] { "bob" }),
            ct);

        var created = Assert.IsType<ObjectResult>(result);

        var group = Assert.IsType<
            ConversationsController.ConversationResponse>(
                created.Value);

        var memberController = CreateController(
            db,
            "acc_bob",
            services);

        var memberResult =
            await memberController.GetConversation(
                group.ConversationId,
                ct);

        var memberOk =
            Assert.IsType<OkObjectResult>(memberResult);

        var memberResponse = Assert.IsType<
            ConversationsController.ConversationResponse>(
                memberOk.Value);

        Assert.Equal("Member", memberResponse.MyRole);

        var ownerResult =
            await ownerController.GetConversation(
                group.ConversationId,
                ct);

        var ownerOk =
            Assert.IsType<OkObjectResult>(ownerResult);

        var ownerResponse = Assert.IsType<
            ConversationsController.ConversationResponse>(
                ownerOk.Value);

        Assert.Equal("Owner", ownerResponse.MyRole);
    }

    [Fact]
    public async Task GetConversation_NonMember_CannotAccessGroup()
    {
        var ct = TestContext.Current.CancellationToken;

        var (db, connection) =
            TestDbContextFactory.CreateInMemoryDbContext();

        await using var connectionScope = connection;
        await using var dbScope = db;

        using var services = CreateServices();

        await AddUsersAsync(db, ct);

        var ownerController = CreateController(
            db,
            "acc_alice",
            services);

        var result = await ownerController.CreateGroup(
            new ConversationsController.CreateGroupRequest(
                "Private group",
                new[] { "bob" }),
            ct);

        var created = Assert.IsType<ObjectResult>(result);

        var group = Assert.IsType<
            ConversationsController.ConversationResponse>(
                created.Value);

        var outsiderController = CreateController(
            db,
            "acc_charlie",
            services);

        var outsiderResult =
            await outsiderController.GetConversation(
                group.ConversationId,
                ct);

        Assert.IsType<NotFoundResult>(outsiderResult);

        var listResult =
            await outsiderController.GetMyConversations(ct);

        var listOk =
            Assert.IsType<OkObjectResult>(listResult);

        var conversations = Assert.IsAssignableFrom<
            IEnumerable<
                ConversationsController.ConversationResponse>>(
                    listOk.Value);

        Assert.Empty(conversations);
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSignalR();

        return services.BuildServiceProvider();
    }

    private static ConversationsController CreateController(
        AppDbContext db,
        string accountId,
        IServiceProvider services)
    {
        var hubContext =
            services.GetRequiredService<IHubContext<ChatHub>>();

        var publisher =
            new ChatEventPublisher(hubContext);

        return new ConversationsController(db, publisher)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            new[]
                            {
                                new Claim(
                                    JwtRegisteredClaimNames.Sub,
                                    accountId)
                            },
                            "Test"))
                }
            }
        };
    }

    private static async Task AddUsersAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        db.Users.AddRange(
            new User(
                "acc_alice",
                "alice",
                "Alice",
                "hash"),

            new User(
                "acc_bob",
                "bob",
                "Bob",
                "hash"),

            new User(
                "acc_charlie",
                "charlie",
                "Charlie",
                "hash"));

        await db.SaveChangesAsync(cancellationToken);
    }
}
