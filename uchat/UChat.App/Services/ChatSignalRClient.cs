
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using UChat.App.Models;

namespace UChat.App.Services;

public sealed class ChatSignalRClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public event Action<ChatMessage>? MessageReceived;
    public event Action<ChatMessage>? MessageEdited;
    public event Action<Guid, Guid>? MessageDeleted;
    public event Action? Reconnected;

    public ChatSignalRClient()
    {
        _connection = new HubConnectionBuilder()
            .WithUrl("http://localhost:5049/hubs/chat", options =>
            {
                options.AccessTokenProvider =
                    () => Task.FromResult(AuthSession.AccessToken);
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<ChatMessage>("MessageCreated", message =>
        {
            MessageReceived?.Invoke(message);
        });

        _connection.On<ChatMessage>("MessageEdited", message =>
        {
            MessageEdited?.Invoke(message);
        });

        _connection.On<MessageDeletedPayload>("MessageDeleted", payload =>
        {
            MessageDeleted?.Invoke(payload.ConversationId, payload.MessageId);
        });

        _connection.Reconnected += _ =>
        {
            Reconnected?.Invoke();
            return Task.CompletedTask;
        };
    }

    public bool IsConnected =>
        _connection.State == HubConnectionState.Connected;

    public async Task ConnectAsync()
    {
        if (_connection.State == HubConnectionState.Disconnected)
            await _connection.StartAsync();
    }

    public Task JoinConversationAsync(Guid conversationId) =>
        _connection.InvokeAsync("JoinConversation", conversationId);

    public Task LeaveConversationAsync(Guid conversationId) =>
        _connection.InvokeAsync("LeaveConversation", conversationId);

    public Task SendMessageAsync(Guid conversationId, string content) =>
        _connection.InvokeAsync(
            "SendMessage",
            conversationId,
            content,
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow);

    public Task EditMessageAsync(Guid conversationId, Guid messageId, string newContent) =>
        _connection.InvokeAsync("EditMessage", conversationId, messageId, newContent);

    public Task DeleteMessageAsync(Guid conversationId, Guid messageId) =>
        _connection.InvokeAsync("DeleteMessage", conversationId, messageId);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private sealed record MessageDeletedPayload(Guid ConversationId, Guid MessageId);
}
