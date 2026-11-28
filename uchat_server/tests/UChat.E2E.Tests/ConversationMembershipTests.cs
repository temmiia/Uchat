using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using UChat.Application.Messages;

namespace UChat.E2E.Tests;

public sealed class ConversationMembershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonMember_CannotAccessConversation(
        bool isGroup)
    {
        await using var server = new TestServerFixture();

        await server.StartAsync();

        await using var alice = new TestChatClient(
            server.BaseAddress, "alice", "Alice");

        await using var bob = new TestChatClient(
            server.BaseAddress, "bob", "Bob");

        await using var charlie = new TestChatClient(
            server.BaseAddress, "charlie", "Charlie");

        foreach (var client in new[]
                 {
                     alice, bob, charlie
                 })
        {
            await client.RegisterAsync();
            await client.LoginAsync();
            await client.ConnectAsync();
        }

        var conversationId = isGroup
            ? await alice.CreateGroupAsync(
                "Private group", "bob")
            : await alice.CreateDirectAsync("bob");

        await alice.JoinAsync(conversationId);
        await bob.JoinAsync(conversationId);

        var conversationUrl =
            $"api/conversations/{conversationId}";

        var messagesUrl =
            $"{conversationUrl}/messages";

        var originalMessage =
            await alice.SendRestAsync(
                conversationId,
                "Private message");

        var messageUrl =
            $"{messagesUrl}/{originalMessage.MessageId}";

        // Members can access the conversation.
        using (var response =
               await bob.Http.GetAsync(conversationUrl, TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);
        }

        var bobHistory =
            await bob.GetHistoryAsync(conversationId);

        Assert.Equal(
            originalMessage.MessageId,
            Assert.Single(bobHistory).MessageId);

        // Non-member cannot discover the conversation.
        using (var response =
               await charlie.Http.GetAsync(conversationUrl, TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);
        }

        var charlieConversations =
            await charlie.Http.GetFromJsonAsync<
                TestChatClient.ConversationResult[]>("api/conversations", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(charlieConversations);

        Assert.DoesNotContain(
            charlieConversations,
            conversation =>
                conversation.ConversationId ==
                conversationId);

        // Non-member cannot read messages.
        using (var response =
               await charlie.Http.GetAsync(messagesUrl, TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                HttpStatusCode.Forbidden,
                response.StatusCode);
        }

        // Non-member cannot send messages.
        using (var response =
               await charlie.Http.PostAsJsonAsync(messagesUrl, new
               {
                   Content = "Unauthorized message",
                   ClientMessageId =
                           Guid.NewGuid().ToString("N"),
                   CreatedAtUtc = DateTime.UtcNow
               }, cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                HttpStatusCode.Forbidden,
                response.StatusCode);
        }

        // Non-member cannot edit messages.
        using (var response =
               await charlie.Http.PatchAsJsonAsync(messageUrl, new
               {
                   Content = "Unauthorized edit"
               }, cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                HttpStatusCode.Forbidden,
                response.StatusCode);
        }

        // Non-member cannot delete messages.
        using (var response =
               await charlie.Http.DeleteAsync(messageUrl, TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                HttpStatusCode.Forbidden,
                response.StatusCode);
        }

        // Non-member cannot join the SignalR group.
        await AssertAccessDeniedAsync(
            () => charlie.JoinAsync(conversationId));

        // Non-member cannot read history via SignalR.
        await AssertAccessDeniedAsync(
            () => charlie.GetHubHistoryAsync(
                conversationId));

        // Non-member cannot send via SignalR.
        await AssertAccessDeniedAsync(
            () => charlie.SendAsync(
                conversationId,
                "Unauthorized SignalR message"));

        // Non-member cannot edit via SignalR.
        await AssertAccessDeniedAsync(
            () => charlie.EditAsync(
                conversationId,
                originalMessage.MessageId,
                "Unauthorized SignalR edit"));

        // Non-member cannot delete via SignalR.
        await AssertAccessDeniedAsync(
            () => charlie.DeleteAsync(
                conversationId,
                originalMessage.MessageId));

        // Bob is a member, but not the message author.
        using (var response =
               await bob.Http.PatchAsJsonAsync(messageUrl, new
               {
                   Content = "Bob edits Alice message"
               }, cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                HttpStatusCode.Forbidden,
                response.StatusCode);
        }

        using (var response =
               await bob.Http.DeleteAsync(messageUrl, TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                HttpStatusCode.Forbidden,
                response.StatusCode);
        }

        await Assert.ThrowsAsync<HubException>(
            () => bob.EditAsync(
                conversationId,
                originalMessage.MessageId,
                "Bob edits Alice message"));

        await Assert.ThrowsAsync<HubException>(
            () => bob.DeleteAsync(
                conversationId,
                originalMessage.MessageId));

        // Verify that denied operations did not
        // modify the database.
        var history =
            await alice.GetHistoryAsync(conversationId);

        var savedMessage = Assert.Single(history);

        Assert.Equal(
            originalMessage.MessageId,
            savedMessage.MessageId);

        Assert.Equal(
            "Private message",
            savedMessage.Content);

        Assert.Null(savedMessage.EditedAtUtc);

        // Verify that Charlie receives no live messages.
        var unauthorizedEvent =
            new TaskCompletionSource<MessageResponse>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        using var charlieSubscription =
            charlie.Hub.On<MessageResponse>(
                "MessageCreated",
                message =>
                {
                    if (message.ConversationId ==
                        conversationId)
                    {
                        unauthorizedEvent
                            .TrySetResult(message);
                    }
                });

        var authorizedEvent =
            new TaskCompletionSource<MessageResponse>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        using var bobSubscription =
            bob.Hub.On<MessageResponse>(
                "MessageCreated",
                message =>
                {
                    if (message.ConversationId ==
                        conversationId)
                    {
                        authorizedEvent
                            .TrySetResult(message);
                    }
                });

        await alice.SendAsync(
            conversationId,
            "Members only");

        var received = await authorizedEvent.Task
            .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.Equal(
            "Members only",
            received.Content);

        var completed = await Task.WhenAny(
            unauthorizedEvent.Task,
            Task.Delay(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken));

        Assert.NotSame(
            unauthorizedEvent.Task,
            completed);
    }

    [Fact]
    public async Task AnonymousClient_CannotAccessProtectedEndpoints()
    {
        await using var server = new TestServerFixture();

        await server.StartAsync();

        using var http = new HttpClient
        {
            BaseAddress = server.BaseAddress,
            Timeout = TimeSpan.FromSeconds(15)
        };

        var conversationId = Guid.NewGuid();

        using (var response = await http.GetAsync("api/conversations", TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);
        }

        using (var response = await http.GetAsync($"api/conversations/{conversationId}", TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);
        }

        using (var response = await http.GetAsync($"api/conversations/{conversationId}/messages", TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);
        }

        using (var response = await http.PostAsJsonAsync($"api/conversations/{conversationId}/messages", new
        {
            Content = "Anonymous message"
        }, cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);
        }

        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(
                server.BaseAddress,
                "hubs/chat"))
            .Build();

        using var timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(15));

        var exception =
            await Assert.ThrowsAsync<HttpRequestException>(
                () => hub.StartAsync(timeout.Token));

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            exception.StatusCode);
    }

    private static async Task AssertAccessDeniedAsync(
        Func<Task> action)
    {
        var exception =
            await Assert.ThrowsAsync<HubException>(
                action);

        Assert.Contains(
            "Access denied",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }
}
