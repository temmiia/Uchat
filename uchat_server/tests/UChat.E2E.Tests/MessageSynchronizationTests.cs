using Microsoft.AspNetCore.SignalR.Client;
using UChat.Application.Messages;

namespace UChat.E2E.Tests;

public sealed class MessageSynchronizationTests
{
    private static readonly TimeSpan EventTimeout =
        TimeSpan.FromSeconds(15);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoClients_SynchronizeMessages_AndPersistAfterRestart(
        bool isGroup)
    {
        await using var server = new TestServerFixture();

        await server.StartAsync();

        await using var alice = new TestChatClient(
            server.BaseAddress,
            "alice",
            "Alice");

        await using var bob = new TestChatClient(
            server.BaseAddress,
            "bob",
            "Bob");

        await alice.RegisterAsync();
        await bob.RegisterAsync();

        await alice.LoginAsync();
        await bob.LoginAsync();

        await alice.ConnectAsync();
        await bob.ConnectAsync();

        var conversationCreated =
            new TaskCompletionSource<Guid>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        using var conversationSubscription =
            bob.Hub.On<Guid>(
                "ConversationCreated",
                id => conversationCreated.TrySetResult(id));

        var conversationId = isGroup
            ? await alice.CreateGroupAsync(
                "E2E group", "bob")
            : await alice.CreateDirectAsync("bob");

        Assert.Equal(
            conversationId,
            await conversationCreated.Task
                .WaitAsync(EventTimeout, TestContext.Current.CancellationToken));

        await alice.JoinAsync(conversationId);
        await bob.JoinAsync(conversationId);

        // Alice sends a message.
        var aliceCreated = NewMessageSource();
        var bobCreated = NewMessageSource();

        using var aliceCreatedSubscription =
            alice.Hub.On<MessageResponse>(
                "MessageCreated",
                message =>
                {
                    if (message.ConversationId ==
                        conversationId)
                    {
                        aliceCreated.TrySetResult(message);
                    }
                });

        using var bobCreatedSubscription =
            bob.Hub.On<MessageResponse>(
                "MessageCreated",
                message =>
                {
                    if (message.ConversationId ==
                        conversationId)
                    {
                        bobCreated.TrySetResult(message);
                    }
                });

        await alice.SendAsync(
            conversationId,
            "Original message");

        var aliceMessage = await aliceCreated.Task
            .WaitAsync(EventTimeout, TestContext.Current.CancellationToken);

        var bobMessage = await bobCreated.Task
            .WaitAsync(EventTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(
            aliceMessage.MessageId,
            bobMessage.MessageId);

        Assert.Equal(
            "Original message",
            bobMessage.Content);

        Assert.Equal(
            "Alice",
            bobMessage.SenderDisplayName);

        var messageId = bobMessage.MessageId;

        // Alice edits her message.
        var aliceEdited = NewMessageSource();
        var bobEdited = NewMessageSource();

        using var aliceEditedSubscription =
            alice.Hub.On<MessageResponse>(
                "MessageEdited",
                message =>
                {
                    if (message.MessageId == messageId)
                        aliceEdited.TrySetResult(message);
                });

        using var bobEditedSubscription =
            bob.Hub.On<MessageResponse>(
                "MessageEdited",
                message =>
                {
                    if (message.MessageId == messageId)
                        bobEdited.TrySetResult(message);
                });

        await alice.EditAsync(
            conversationId,
            messageId,
            "Edited message");

        var aliceEditedMessage =
            await aliceEdited.Task
                .WaitAsync(EventTimeout, TestContext.Current.CancellationToken);

        var bobEditedMessage =
            await bobEdited.Task
                .WaitAsync(EventTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(
            "Edited message",
            aliceEditedMessage.Content);

        Assert.Equal(
            "Edited message",
            bobEditedMessage.Content);

        Assert.NotNull(
            bobEditedMessage.EditedAtUtc);

        Assert.Equal(
            "Alice",
            bobEditedMessage.SenderDisplayName);

        // Bob sends a message that will be deleted.
        var messageToDeleteSource = NewMessageSource();

        using var deleteCandidateSubscription =
            alice.Hub.On<MessageResponse>(
                "MessageCreated",
                message =>
                {
                    if (message.ConversationId ==
                        conversationId &&
                        message.Content == "Delete me")
                    {
                        messageToDeleteSource
                            .TrySetResult(message);
                    }
                });

        await bob.SendAsync(
            conversationId,
            "Delete me");

        var messageToDelete =
            await messageToDeleteSource.Task
                .WaitAsync(EventTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(
            "Bob",
            messageToDelete.SenderDisplayName);

        var deletedMessageId =
            messageToDelete.MessageId;

        var aliceDeleted =
            NewDeletedMessageSource();

        var bobDeleted =
            NewDeletedMessageSource();

        using var aliceDeletedSubscription =
            alice.Hub.On<TestChatClient.DeletedMessageEvent>(
                "MessageDeleted",
                message =>
                {
                    if (message.MessageId ==
                        deletedMessageId)
                    {
                        aliceDeleted.TrySetResult(message);
                    }
                });

        using var bobDeletedSubscription =
            bob.Hub.On<TestChatClient.DeletedMessageEvent>(
                "MessageDeleted",
                message =>
                {
                    if (message.MessageId ==
                        deletedMessageId)
                    {
                        bobDeleted.TrySetResult(message);
                    }
                });

        await bob.DeleteAsync(
            conversationId,
            deletedMessageId);

        var aliceDeletedEvent =
            await aliceDeleted.Task
                .WaitAsync(EventTimeout, TestContext.Current.CancellationToken);

        var bobDeletedEvent =
            await bobDeleted.Task
                .WaitAsync(EventTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(
            conversationId,
            aliceDeletedEvent.ConversationId);

        Assert.Equal(
            deletedMessageId,
            bobDeletedEvent.MessageId);

        // Verify history before restart.
        var beforeRestart =
            await alice.GetHistoryAsync(conversationId);

        var savedMessage = Assert.Single(beforeRestart);

        Assert.Equal(
            messageId,
            savedMessage.MessageId);

        Assert.Equal(
            "Edited message",
            savedMessage.Content);

        Assert.DoesNotContain(
            beforeRestart,
            message => message.MessageId ==
                deletedMessageId);

        // Restart the actual server process.
        await alice.DisconnectAsync();
        await bob.DisconnectAsync();

        await server.RestartAsync();

        // Login again using persisted accounts.
        await alice.LoginAsync();
        await bob.LoginAsync();

        await alice.ConnectAsync();
        await bob.ConnectAsync();

        await alice.JoinAsync(conversationId);
        await bob.JoinAsync(conversationId);

        // Verify history through both transports.
        var aliceHistory =
            await alice.GetHistoryAsync(conversationId);

        var bobHistory =
            await bob.GetHubHistoryAsync(conversationId);

        AssertPersistedMessage(
            aliceHistory,
            messageId,
            deletedMessageId);

        AssertPersistedMessage(
            bobHistory,
            messageId,
            deletedMessageId);

        // Verify live communication after restart.
        var afterRestart = NewMessageSource();

        using var afterRestartSubscription =
            alice.Hub.On<MessageResponse>(
                "MessageCreated",
                message =>
                {
                    if (message.ConversationId ==
                        conversationId &&
                        message.Content ==
                        "Message after restart")
                    {
                        afterRestart.TrySetResult(message);
                    }
                });

        await bob.SendAsync(
            conversationId,
            "Message after restart");

        var receivedAfterRestart =
            await afterRestart.Task
                .WaitAsync(EventTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(
            "Message after restart",
            receivedAfterRestart.Content);

        Assert.Equal(
            "Bob",
            receivedAfterRestart.SenderDisplayName);

        var finalHistory =
            await alice.GetHistoryAsync(conversationId);

        Assert.Equal(2, finalHistory.Length);
    }

    private static void AssertPersistedMessage(
        MessageResponse[] history,
        Guid messageId,
        Guid deletedMessageId)
    {
        var message = Assert.Single(history);

        Assert.Equal(
            messageId,
            message.MessageId);

        Assert.Equal(
            "Edited message",
            message.Content);

        Assert.Equal(
            "Alice",
            message.SenderDisplayName);

        Assert.NotNull(message.EditedAtUtc);

        Assert.DoesNotContain(
            history,
            item => item.MessageId == deletedMessageId);
    }

    private static TaskCompletionSource<MessageResponse>
        NewMessageSource()
    {
        return new TaskCompletionSource<MessageResponse>(
            TaskCreationOptions
                .RunContinuationsAsynchronously);
    }

    private static TaskCompletionSource<
        TestChatClient.DeletedMessageEvent>
        NewDeletedMessageSource()
    {
        return new TaskCompletionSource<
            TestChatClient.DeletedMessageEvent>(
            TaskCreationOptions
                .RunContinuationsAsynchronously);
    }
}