using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using UChat.App.Models;

namespace UChat.App.Services;

public sealed class ChatSignalRClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public event Action<ChatMessage>? MessageReceived;
    public event Action<ChatMessage>? MessageEdited;
    public event Action<MessageDeletedEvent>? MessageDeleted;
    public event Action? ConversationCreated;
    public event Action? Reconnected;

    public ChatSignalRClient()
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(NetworkConfig.HubUrl, options =>
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

        _connection.On<MessageDeletedEvent>("MessageDeleted", message =>
        {
            MessageDeleted?.Invoke(message);
        });

        _connection.On<Guid>("ConversationCreated", _ =>
        {
            ConversationCreated?.Invoke();
        });

        _connection.Reconnected += _ =>
        {
            Reconnected?.Invoke();
            return Task.CompletedTask;
        };

        _connection.Closed += _ =>
        {
            return Task.CompletedTask;
        };
    }

    public bool IsConnected =>
        _connection.State == HubConnectionState.Connected;

    public async Task ConnectAsync()
    {
        await _connectLock.WaitAsync();

        try
        {
            if (_connection.State == HubConnectionState.Disconnected)
            {
                await _connection.StartAsync();
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public Task JoinConversationAsync(Guid conversationId) =>
        _connection.InvokeAsync(
            "JoinConversation",
            conversationId);

    public Task LeaveConversationAsync(Guid conversationId) =>
        _connection.InvokeAsync(
            "LeaveConversation",
            conversationId);

    public Task SendMessageAsync(
        Guid conversationId,
        string content) =>
        _connection.InvokeAsync(
            "SendMessage",
            conversationId,
            content,
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow);

    public async ValueTask DisposeAsync()
    {
        await _connectLock.WaitAsync();

        try
        {
            await _connection.DisposeAsync();
        }
        finally
        {
            _connectLock.Release();
        }
    }
}
