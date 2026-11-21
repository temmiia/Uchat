
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UChat.App.Models;
using UChat.App.Services;

namespace UChat.App.ViewModels;

public partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ConversationsApiClient _conversationsApi = new();
    private readonly MessagesApiClient _messagesApi = new();
    private readonly ChatSignalRClient _signalR = new();

    public ObservableCollection<ChatConversation> Conversations { get; } = new();
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    [ObservableProperty]
    private ChatConversation? selectedConversation;

    [ObservableProperty]
    private string newChatUsername = "";

    [ObservableProperty]
    private string messageText = "";

    [ObservableProperty]
    private string status = "Connecting...";

    public MainViewModel()
    {
        _signalR.MessageReceived += OnMessageReceived;
        _signalR.Reconnected += OnReconnected;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _signalR.ConnectAsync();
            Status = "Connected";

            await RefreshConversationsAsync();
        }
        catch (Exception ex)
        {
            Status = $"Connection error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshConversationsAsync()
    {
        try
        {
            var conversations = await _conversationsApi.GetConversationsAsync();

            Conversations.Clear();

            foreach (var conversation in conversations)
                Conversations.Add(conversation);
        }
        catch (Exception ex)
        {
            Status = $"Load error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreateChatAsync()
    {
        var username = NewChatUsername.Trim();

        if (username.Length == 0)
            return;

        try
        {
            var conversation = await _conversationsApi.CreateDirectAsync(username);

            await RefreshConversationsAsync();

            NewChatUsername = "";

            SelectedConversation = Conversations.FirstOrDefault(
                c => c.ConversationId == conversation.ConversationId);
        }
        catch (Exception ex)
        {
            Status = $"Create chat error: {ex.Message}";
        }
    }

    partial void OnSelectedConversationChanged(ChatConversation? value)
    {
        _ = SelectConversationAsync(value);
    }

    private async Task SelectConversationAsync(ChatConversation? conversation)
    {
        try
        {
            Messages.Clear();

            if (conversation is null)
                return;

            if (!_signalR.IsConnected)
                await _signalR.ConnectAsync();

            await _signalR.JoinConversationAsync(conversation.ConversationId);

            var history = await _messagesApi.GetMessagesAsync(
                conversation.ConversationId);

            if (SelectedConversation?.ConversationId != conversation.ConversationId)
                return;

            foreach (var message in history.OrderBy(m => m.CreatedAtUtc))
                AddMessage(message);

            Status = $"Chat: {conversation.DisplayName}";
        }
        catch (Exception ex)
        {
            Status = $"Chat error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var conversation = SelectedConversation;
        var content = MessageText.Trim();

        if (conversation is null || content.Length == 0)
            return;

        try
        {
            await _signalR.SendMessageAsync(conversation.ConversationId, content);
            MessageText = "";
        }
        catch (Exception ex)
        {
            Status = $"Send error: {ex.Message}";
        }
    }

    private void OnMessageReceived(ChatMessage message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (SelectedConversation?.ConversationId == message.ConversationId)
                AddMessage(message);
        });
    }

    private void AddMessage(ChatMessage message)
    {
        if (Messages.Any(m => m.MessageId == message.MessageId))
            return;

        Messages.Add(message);
    }

    private void OnReconnected()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _ = RejoinAsync();
        });
    }

    private async Task RejoinAsync()
    {
        var conversation = SelectedConversation;

        if (conversation is null)
            return;

        try
        {
            await _signalR.JoinConversationAsync(conversation.ConversationId);

            var history = await _messagesApi.GetMessagesAsync(
                conversation.ConversationId);

            if (SelectedConversation?.ConversationId != conversation.ConversationId)
                return;

            foreach (var message in history.OrderBy(m => m.CreatedAtUtc))
                AddMessage(message);

            Status = "Reconnected";
        }
        catch (Exception ex)
        {
            Status = $"Reconnect error: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        _signalR.MessageReceived -= OnMessageReceived;
        _signalR.Reconnected -= OnReconnected;
        await _signalR.DisposeAsync();
    }
}
