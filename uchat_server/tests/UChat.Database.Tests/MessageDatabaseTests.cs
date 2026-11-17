using Microsoft.EntityFrameworkCore;
using UChat.Domain.Entities;
using UChat.Domain.Enums;

namespace UChat.Database.Tests;

public class MessageDatabaseTests
{
    [Fact]
    public async Task Can_Send_Edit_And_Soft_Delete_Message()
    {
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var sender = new User("acc_sender", "sender_user", "Sender Display", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.Add(sender);
        context.Conversations.Add(conversation);
        context.Members.Add(new ConversationMember(conversation.ConversationId, sender.AccountId, MemberRole.Owner));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // 1. Отправка сообщения
        var message = new Message(
            conversation.ConversationId,
            sender.AccountId,
            content: "Hello world!",
            clientMessageId: "client-req-001"
        );
        context.Messages.Add(message);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var retrieved = await context.Messages
            .Include(m => m.Sender)
            .FirstOrDefaultAsync(m => m.MessageId == message.MessageId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(retrieved);
        Assert.Equal("Hello world!", retrieved.Content);
        Assert.Equal("Sender Display", retrieved.Sender.DisplayName);
        Assert.Equal("sender_user", retrieved.Sender.UserName);
        Assert.False(retrieved.IsDeleted);
        Assert.Null(retrieved.EditedAtUtc);

        // 2. Редактирование текста сообщения
        retrieved.EditContent("Hello world (edited)!");
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var edited = await context.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.MessageId == message.MessageId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(edited);
        Assert.Equal("Hello world (edited)!", edited.Content);
        Assert.NotNull(edited.EditedAtUtc);
        Assert.False(edited.IsDeleted);

        // 3. Мягкое удаление (помечаем как удаленное и затираем текст)
        retrieved.MarkAsDeleted();
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var deleted = await context.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.MessageId == message.MessageId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(deleted);
        Assert.True(deleted.IsDeleted);
        Assert.Empty(deleted.Content);
    }

    [Fact]
    public async Task Messages_Are_Retrieved_In_Chronological_Order()
    {
        // Проверяем сортировку сообщений по дате создания
        var (context, connection) = TestDbContextFactory.CreateInMemoryDbContext();
        await using var _ = connection;
        await using var __ = context;

        var user = new User("acc_user1", "alice", "Alice", "hash");
        var conversation = new Conversation(ConversationType.Direct);
        context.Users.Add(user);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var msg1 = new Message(conversation.ConversationId, user.AccountId, "Msg 1");
        var msg2 = new Message(conversation.ConversationId, user.AccountId, "Msg 2");
        var msg3 = new Message(conversation.ConversationId, user.AccountId, "Msg 3");

        context.Messages.AddRange(msg1, msg2, msg3);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var history = await context.Messages
            .Where(m => m.ConversationId == conversation.ConversationId)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, history.Count);
        Assert.Equal("Msg 1", history[0].Content);
        Assert.Equal("Msg 2", history[1].Content);
        Assert.Equal("Msg 3", history[2].Content);
    }
}
