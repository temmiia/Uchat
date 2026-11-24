using UChat.Application.Messages;
using UChat.Domain.Entities;
using UChat.Domain.Enums;
using UChat.Infrastructure.Repositories;

namespace UChat.Database.Tests;

public class MessageRepositoryTests
{
    [Fact]
    public async Task CreateAsync_And_GetByConversationIdAsync_Saves_And_Retrieves_Messages()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user = new User("acc_sender_1", "john_sender", "John Sender", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.Add(user);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfMessageRepository(context);
        var messageService = new MessageService(repo);

        // Save messages
        var (msg1, dup1) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            user.AccountId,
            "First message",
            clientMessageId: "c_1",
            cancellationToken: TestContext.Current.CancellationToken);

        var (msg2, dup2) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            user.AccountId,
            "Second message",
            clientMessageId: "c_2",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(dup1);
        Assert.False(dup2);

        // Retrieve messages from database
        var messages = await messageService.GetMessagesAsync(
            conversation.ConversationId,
            limit: 50,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, messages.Count);
        Assert.Equal("First message", messages[0].Content);
        Assert.Equal("Second message", messages[1].Content);
        Assert.Equal(user.AccountId, messages[0].SenderAccountId);
    }

    [Fact]
    public async Task Duplicate_ClientMessageId_Prevents_Duplicate_And_Returns_Existing_Message()
    {
        // Verify idempotency via ClientMessageId: retried requests must return existing message without creating duplicates
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user = new User("acc_idemp_user", "idemp", "Idemp User", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.Add(user);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfMessageRepository(context);
        var messageService = new MessageService(repo);

        var clientMsgId = "offline-retry-uuid-001";

        // 1st attempt: original send
        var (firstResponse, firstIsDuplicate) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            user.AccountId,
            "Message sent while unstable network",
            clientMessageId: clientMsgId,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(firstIsDuplicate);

        // 2nd attempt: client retries with the same ClientMessageId after reconnection
        var (secondResponse, secondIsDuplicate) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            user.AccountId,
            "Message sent while unstable network",
            clientMessageId: clientMsgId,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(secondIsDuplicate);
        Assert.Equal(firstResponse.MessageId, secondResponse.MessageId);

        // Verify only 1 record exists in the database
        var allMessages = await messageService.GetMessagesAsync(
            conversation.ConversationId,
            limit: 50,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(allMessages);
    }

    [Fact]
    public async Task Offline_Message_Preserves_Client_Creation_Timestamp()
    {
        // When client sends message created while offline (e.g. 20:44), the original timestamp must be preserved
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user = new User("acc_offline_user", "offline_user", "Offline User", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.Add(user);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfMessageRepository(context);
        var messageService = new MessageService(repo);

        var offlineTimestamp = DateTime.UtcNow.AddMinutes(-6); // Typed offline 6 minutes ago

        var (savedMessage, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            user.AccountId,
            "Offline typed message",
            clientMessageId: "offline-msg-002",
            clientCreatedAtUtc: offlineTimestamp,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(offlineTimestamp, savedMessage.CreatedAtUtc);
        Assert.NotNull(savedMessage.ServerReceivedAtUtc);
        Assert.True(savedMessage.ServerReceivedAtUtc > savedMessage.CreatedAtUtc);
    }

    [Fact]
    public async Task Reconnect_Catchup_Loads_Messages_After_Specified_Timestamp()
    {
        // On reconnect, client requests messages after its last seen timestamp to catch up
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user = new User("acc_reconnect_user", "reconnect_user", "Reconnect User", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.Add(user);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfMessageRepository(context);

        var baseTime = DateTime.UtcNow.AddMinutes(-10);
        var msg1 = new Message(conversation.ConversationId, user.AccountId, "Old Msg 1", createdAtUtc: baseTime);
        var msg2 = new Message(conversation.ConversationId, user.AccountId, "Old Msg 2", createdAtUtc: baseTime.AddMinutes(2));
        var msg3 = new Message(conversation.ConversationId, user.AccountId, "New Msg 3 (during disconnect)", createdAtUtc: baseTime.AddMinutes(5));
        var msg4 = new Message(conversation.ConversationId, user.AccountId, "New Msg 4 (during disconnect)", createdAtUtc: baseTime.AddMinutes(8));

        context.Messages.AddRange(msg1, msg2, msg3, msg4);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var messageService = new MessageService(repo);

        // Client reconnects having seen messages up to baseTime + 2 minutes
        var lastSeenTimestamp = baseTime.AddMinutes(2);
        var newMessages = await messageService.GetMessagesAsync(
            conversation.ConversationId,
            limit: 50,
            afterUtc: lastSeenTimestamp,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, newMessages.Count);
        Assert.Equal("New Msg 3 (during disconnect)", newMessages[0].Content);
        Assert.Equal("New Msg 4 (during disconnect)", newMessages[1].Content);
    }

    [Fact]
    public async Task GetByConversationIdAsync_Excludes_Deleted_Messages()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user = new User("acc_del_test", "alice", "Alice", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.Add(user);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfMessageRepository(context);
        var messageService = new MessageService(repo);

        var (activeMsg, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            user.AccountId,
            "Active message",
            cancellationToken: TestContext.Current.CancellationToken);

        var msgToDelete = new Message(conversation.ConversationId, user.AccountId, "To be deleted");
        msgToDelete.MarkAsDeleted();
        await repo.CreateAsync(msgToDelete, TestContext.Current.CancellationToken);

        var retrieved = await messageService.GetMessagesAsync(
            conversation.ConversationId,
            limit: 50,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(retrieved);
        Assert.Equal(activeMsg.MessageId, retrieved[0].MessageId);
        Assert.Equal("Active message", retrieved[0].Content);
    }

    [Fact]
    public async Task GetByConversationIdAsync_Supports_Limit_And_Chronological_Order()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user = new User("acc_order_test", "bob", "Bob", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.Add(user);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfMessageRepository(context);

        // Add 5 messages in sequence
        for (int i = 1; i <= 5; i++)
        {
            var msg = new Message(conversation.ConversationId, user.AccountId, $"Message #{i}");
            context.Messages.Add(msg);
        }
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var messageService = new MessageService(repo);

        // Request the last 3 messages
        var messages = await messageService.GetMessagesAsync(
            conversation.ConversationId,
            limit: 3,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, messages.Count);
        // Expect messages #3, #4, #5 in chronological order
        Assert.Equal("Message #3", messages[0].Content);
        Assert.Equal("Message #4", messages[1].Content);
        Assert.Equal("Message #5", messages[2].Content);
    }

    [Fact]
    public async Task SaveMessageAsync_Throws_On_Empty_Content_Or_Invalid_Conversation()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var repo = new EfMessageRepository(context);
        var messageService = new MessageService(repo);

        // Empty message content
        await Assert.ThrowsAsync<ArgumentException>(() =>
            messageService.SaveMessageAsync(
                Guid.NewGuid(),
                "acc_test",
                "",
                cancellationToken: TestContext.Current.CancellationToken));

        // Empty conversation GUID
        await Assert.ThrowsAsync<ArgumentException>(() =>
            messageService.SaveMessageAsync(
                Guid.Empty,
                "acc_test",
                "Test message",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EditMessageAsync_Updates_Content_And_Sets_EditedAtUtc()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user = new User("acc_editor_1", "john_editor", "John Editor", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.Add(user);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfMessageRepository(context);
        var messageService = new MessageService(repo);

        var (created, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            user.AccountId,
            "Original content",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(created.EditedAtUtc);

        var edited = await messageService.EditMessageAsync(
            conversation.ConversationId,
            created.MessageId,
            user.AccountId,
            "Updated content",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Updated content", edited.Content);
        Assert.NotNull(edited.EditedAtUtc);

        var inDb = await repo.GetByIdAsync(created.MessageId, TestContext.Current.CancellationToken);
        Assert.NotNull(inDb);
        Assert.Equal("Updated content", inDb.Content);
        Assert.NotNull(inDb.EditedAtUtc);
    }

    [Fact]
    public async Task EditMessageAsync_Throws_When_Not_Author()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var author = new User("acc_author", "author_usr", "Author", "hash");
        var stranger = new User("acc_stranger", "stranger_usr", "Stranger", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.AddRange(author, stranger);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfMessageRepository(context);
        var messageService = new MessageService(repo);

        var (created, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            author.AccountId,
            "Author's secret",
            cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            messageService.EditMessageAsync(
                conversation.ConversationId,
                created.MessageId,
                stranger.AccountId,
                "Hacked content",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EditMessageAsync_Throws_When_Message_Is_Deleted()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var author = new User("acc_del_author", "del_author", "Author", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.Add(author);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfMessageRepository(context);
        var messageService = new MessageService(repo);

        var (created, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            author.AccountId,
            "About to be deleted",
            cancellationToken: TestContext.Current.CancellationToken);

        await messageService.DeleteMessageAsync(
            conversation.ConversationId,
            created.MessageId,
            author.AccountId,
            cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            messageService.EditMessageAsync(
                conversation.ConversationId,
                created.MessageId,
                author.AccountId,
                "Try editing deleted",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EditMessageAsync_Throws_On_Empty_Content()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var author = new User("acc_author_empty", "author_empty", "Author", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.Add(author);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfMessageRepository(context);
        var messageService = new MessageService(repo);

        var (created, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            author.AccountId,
            "Valid content",
            cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            messageService.EditMessageAsync(
                conversation.ConversationId,
                created.MessageId,
                author.AccountId,
                "   ",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteMessageAsync_Soft_Deletes_Message_And_Excludes_From_History()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user = new User("acc_deleter", "deleter_usr", "Deleter User", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.Add(user);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfMessageRepository(context);
        var messageService = new MessageService(repo);

        var (msg1, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            user.AccountId,
            "Keep this message",
            cancellationToken: TestContext.Current.CancellationToken);

        var (msg2, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            user.AccountId,
            "Delete this message",
            cancellationToken: TestContext.Current.CancellationToken);

        // Delete msg2
        await messageService.DeleteMessageAsync(
            conversation.ConversationId,
            msg2.MessageId,
            user.AccountId,
            cancellationToken: TestContext.Current.CancellationToken);

        // Check in DB directly: soft deleted, content wiped
        var inDb = await repo.GetByIdAsync(msg2.MessageId, TestContext.Current.CancellationToken);
        Assert.NotNull(inDb);
        Assert.True(inDb.IsDeleted);
        Assert.Equal(string.Empty, inDb.Content);

        // Check history query: excluded
        var activeMessages = await messageService.GetMessagesAsync(
            conversation.ConversationId,
            limit: 50,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(activeMessages);
        Assert.Equal(msg1.MessageId, activeMessages[0].MessageId);
    }

    [Fact]
    public async Task DeleteMessageAsync_Throws_When_Not_Author()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var author = new User("acc_del_author2", "del_author2", "Author", "hash");
        var stranger = new User("acc_del_stranger", "del_stranger", "Stranger", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.AddRange(author, stranger);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfMessageRepository(context);
        var messageService = new MessageService(repo);

        var (created, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            author.AccountId,
            "Do not touch",
            cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            messageService.DeleteMessageAsync(
                conversation.ConversationId,
                created.MessageId,
                stranger.AccountId,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteMessageAsync_Is_Idempotent_When_Already_Deleted()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var author = new User("acc_idem_author", "idem_author", "Author", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.Add(author);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new EfMessageRepository(context);
        var messageService = new MessageService(repo);

        var (created, _) = await messageService.SaveMessageAsync(
            conversation.ConversationId,
            author.AccountId,
            "Delete twice test",
            cancellationToken: TestContext.Current.CancellationToken);

        // First deletion
        await messageService.DeleteMessageAsync(
            conversation.ConversationId,
            created.MessageId,
            author.AccountId,
            cancellationToken: TestContext.Current.CancellationToken);

        // Second deletion: should succeed without error
        await messageService.DeleteMessageAsync(
            conversation.ConversationId,
            created.MessageId,
            author.AccountId,
            cancellationToken: TestContext.Current.CancellationToken);

        var inDb = await repo.GetByIdAsync(created.MessageId, TestContext.Current.CancellationToken);
        Assert.NotNull(inDb);
        Assert.True(inDb.IsDeleted);
    }
}
