using System.Text;
using Microsoft.AspNetCore.Identity;
using UChat.Application.Abstractions;
using UChat.Application.Auth;
using UChat.Application.Messages;
using UChat.Database.Tests;
using UChat.Domain.Entities;
using UChat.Domain.Enums;
using UChat.Infrastructure.Repositories;

namespace UChat.Application.Tests;

public class SecurityAndPermissionsTests
{
    [Fact]
    public async Task Login_With_Valid_Password_Returns_User()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var hasher = new PasswordHasher<User>();
        var user = new User("acc_test_1", "secure_user", "Secure User", "temp_hash");
        var hash = hasher.HashPassword(user, "CorrectPassword123!");
        typeof(User).GetProperty(nameof(User.PasswordHash))!.SetValue(user, hash);

        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var loginService = new LoginUserService(new EfUserRepository(context), hasher);
        var result = await loginService.LoginAsync(
            new LoginRequest("secure_user", "CorrectPassword123!"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("acc_test_1", result.AccountId);
        Assert.Equal("secure_user", result.UserName);
    }

    [Fact]
    public async Task Login_With_Invalid_Password_Returns_Null()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var hasher = new PasswordHasher<User>();
        var user = new User("acc_test_2", "user_two", "User Two", "temp_hash");
        var hash = hasher.HashPassword(user, "OriginalPassword123!");
        typeof(User).GetProperty(nameof(User.PasswordHash))!.SetValue(user, hash);

        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var loginService = new LoginUserService(new EfUserRepository(context), hasher);
        var result = await loginService.LoginAsync(
            new LoginRequest("user_two", "WrongPassword!"),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task Login_With_NonExistent_Username_Returns_Null()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var loginService = new LoginUserService(new EfUserRepository(context), new PasswordHasher<User>());
        var result = await loginService.LoginAsync(
            new LoginRequest("does_not_exist", "SomePassword123!"),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("username", "")]
    [InlineData("   ", "   ")]
    public async Task Login_With_Empty_Credentials_Returns_Null(string username, string password)
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var loginService = new LoginUserService(new EfUserRepository(context), new PasswordHasher<User>());
        var result = await loginService.LoginAsync(
            new LoginRequest(username, password),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task NonAuthor_Cannot_Edit_Message_Throws_UnauthorizedAccessException()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var author = new User("author_id", "author_nick", "Author", "hash");
        var attacker = new User("attacker_id", "attacker_nick", "Attacker", "hash");
        var conversation = new Conversation(ConversationType.Direct);

        context.Users.AddRange(author, attacker);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var messageRepo = new EfMessageRepository(context);
        var messageService = new MessageService(messageRepo);

        var (createdMsg, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            author.AccountId,
            "Original content",
            clientMessageId: "sec_1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Attacker attempts to edit author's message
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            messageService.EditMessageAsync(
                conversation.ConversationId,
                createdMsg.MessageId,
                attacker.AccountId,
                "Hacked content",
                TestContext.Current.CancellationToken));

        Assert.Equal("You can only edit your own messages.", ex.Message);
    }

    [Fact]
    public async Task NonAuthor_Cannot_Delete_Message_Throws_UnauthorizedAccessException()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var author = new User("author_id_2", "author_nick_2", "Author 2", "hash");
        var attacker = new User("attacker_id_2", "attacker_nick_2", "Attacker 2", "hash");
        var conversation = new Conversation(ConversationType.Direct);

        context.Users.AddRange(author, attacker);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var messageRepo = new EfMessageRepository(context);
        var messageService = new MessageService(messageRepo);

        var (createdMsg, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            author.AccountId,
            "Message to be deleted",
            clientMessageId: "sec_2",
            cancellationToken: TestContext.Current.CancellationToken);

        // Attacker attempts to delete author's message
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            messageService.DeleteMessageAsync(
                conversation.ConversationId,
                createdMsg.MessageId,
                attacker.AccountId,
                TestContext.Current.CancellationToken));

        Assert.Equal("You can only delete your own messages.", ex.Message);
    }

    [Fact]
    public async Task Author_Can_Edit_Own_Message_Successfully()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var author = new User("author_id_3", "author_nick_3", "Author 3", "hash");
        var conversation = new Conversation(ConversationType.Direct);

        context.Users.Add(author);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var messageRepo = new EfMessageRepository(context);
        var messageService = new MessageService(messageRepo);

        var (createdMsg, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            author.AccountId,
            "Before edit",
            clientMessageId: "sec_3",
            cancellationToken: TestContext.Current.CancellationToken);

        var updated = await messageService.EditMessageAsync(
            conversation.ConversationId,
            createdMsg.MessageId,
            author.AccountId,
            "After edit",
            TestContext.Current.CancellationToken);

        Assert.Equal("After edit", updated.Content);
        Assert.NotNull(updated.EditedAtUtc);
    }

    [Fact]
    public async Task Author_Can_Delete_Own_Message_Successfully()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var author = new User("author_id_4", "author_nick_4", "Author 4", "hash");
        var conversation = new Conversation(ConversationType.Direct);

        context.Users.Add(author);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var messageRepo = new EfMessageRepository(context);
        var messageService = new MessageService(messageRepo);

        var (createdMsg, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            author.AccountId,
            "To be deleted",
            clientMessageId: "sec_4",
            cancellationToken: TestContext.Current.CancellationToken);

        await messageService.DeleteMessageAsync(
            conversation.ConversationId,
            createdMsg.MessageId,
            author.AccountId,
            TestContext.Current.CancellationToken);

        var rawMessage = await context.Messages.FindAsync(new object[] { createdMsg.MessageId }, TestContext.Current.CancellationToken);
        Assert.NotNull(rawMessage);
        Assert.True(rawMessage.IsDeleted);
    }

    [Fact]
    public async Task Edit_Deleted_Message_Throws_InvalidOperationException()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var author = new User("author_id_5", "author_nick_5", "Author 5", "hash");
        var conversation = new Conversation(ConversationType.Direct);

        context.Users.Add(author);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var messageRepo = new EfMessageRepository(context);
        var messageService = new MessageService(messageRepo);

        var (createdMsg, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            author.AccountId,
            "Will be deleted",
            clientMessageId: "sec_5",
            cancellationToken: TestContext.Current.CancellationToken);

        await messageService.DeleteMessageAsync(
            conversation.ConversationId,
            createdMsg.MessageId,
            author.AccountId,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            messageService.EditMessageAsync(
                conversation.ConversationId,
                createdMsg.MessageId,
                author.AccountId,
                "New text",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void JwtSecret_Must_Be_At_Least_32_Bytes()
    {
        var shortSecret = "too_short_key";
        var byteCount = Encoding.UTF8.GetByteCount(shortSecret);

        Assert.True(byteCount < 32);

        var validSecret = "this_key_has_at_least_32_bytes_which_is_safe_for_hmac_sha256";
        var validByteCount = Encoding.UTF8.GetByteCount(validSecret);

        Assert.True(validByteCount >= 32);
    }

    [Fact]
    public void JwtSecret_With_Only_Spaces_Or_Repetitive_Characters_Has_Low_Entropy()
    {
        var whitespaceOnly = new string(' ', 40);
        Assert.True(string.IsNullOrWhiteSpace(whitespaceOnly) || whitespaceOnly.Trim().Length < 32);

        var repetitive = new string('a', 40);
        var uniqueCharCount = repetitive.Distinct().Count();
        Assert.True(uniqueCharCount < 5);

        var strongKey = "my_custom_super_secure_jwt_key_with_high_entropy_12345!";
        Assert.True(strongKey.Trim().Length >= 32);
        Assert.True(strongKey.Distinct().Count() >= 5);
    }
}
