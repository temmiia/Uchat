
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
    private const int PageSize = 50;

    private readonly ConversationsApiClient _conversationsApi = new();
    private readonly MessagesApiClient _messagesApi = new();
    private readonly ChatSignalRClient _signalR = new();

    private int _conversationVersion;

    public ObservableCollection<ChatConversation> Conversations { get; } = new();
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadOlderMessagesCommand))]
    private ChatConversation? selectedConversation;

    [ObservableProperty]
    private string newChatUsername = "";

    [ObservableProperty]
    private string messageText = "";

    [ObservableProperty]
    private string status = "Connecting...";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadOlderMessagesCommand))]
    private bool isLoadingHistory;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadOlderMessagesCommand))]
    private bool hasMoreMessages;

    public MainViewModel()
    {
        _signalR.MessageReceived += OnMessageReceived;
        _signalR.MessageEdited += OnMessageEdited;
        _signalR.MessageDeleted += OnMessageDeleted;
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
            var conversations =
                await _conversationsApi.GetConversationsAsync();

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
            var conversation =
                await _conversationsApi.CreateDirectAsync(username);

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
        var version = ++_conversationVersion;

        HasMoreMessages = false;
        IsLoadingHistory = false;
        Messages.Clear();

        _ = SelectConversationAsync(value, version);
    }

    private bool IsCurrentConversation(Guid conversationId, int version)
    {
        return _conversationVersion == version
            && SelectedConversation?.ConversationId == conversationId;
    }

    private async Task SelectConversationAsync(
        ChatConversation? conversation,
        int version)
    {
        if (conversation is null)
            return;

        try
        {
            if (!_signalR.IsConnected)
                await _signalR.ConnectAsync();

            if (!IsCurrentConversation(conversation.ConversationId, version))
                return;

            await _signalR.JoinConversationAsync(conversation.ConversationId);

            if (!IsCurrentConversation(conversation.ConversationId, version))
                return;

            var history = await _messagesApi.GetMessagesAsync(
                conversation.ConversationId,
                limit: PageSize);

            if (!IsCurrentConversation(conversation.ConversationId, version))
                return;

            foreach (var message in history.OrderBy(m => m.CreatedAtUtc))
                AddMessage(message);

            HasMoreMessages = history.Count == PageSize;

            Status = $"Chat: {conversation.DisplayName}";
        }
        catch (Exception ex)
        {
            if (IsCurrentConversation(conversation.ConversationId, version))
                Status = $"Chat error: {ex.Message}";
        }
    }

    private bool CanLoadOlderMessages()
    {
        return SelectedConversation is not null
            && HasMoreMessages
            && !IsLoadingHistory
            && Messages.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(CanLoadOlderMessages))]
    private async Task LoadOlderMessagesAsync()
    {
        var conversation = SelectedConversation;

        if (conversation is null || !CanLoadOlderMessages())
            return;

        var version = _conversationVersion;

        var oldestMessage = Messages
            .OrderBy(m => m.CreatedAtUtc)
            .First();

        IsLoadingHistory = true;

        try
        {
            var olderMessages = await _messagesApi.GetMessagesAsync(
                conversation.ConversationId,
                limit: PageSize,
                beforeUtc: oldestMessage.CreatedAtUtc);

            if (!IsCurrentConversation(conversation.ConversationId, version))
                return;

            var existingIds = Messages
                .Select(m => m.MessageId)
                .ToHashSet();

            // Insert at index 0 in reverse chronological order.
            // The final collection remains oldest -> newest.
            foreach (var message in olderMessages
                .OrderByDescending(m => m.CreatedAtUtc))
            {
                if (existingIds.Add(message.MessageId))
                    Messages.Insert(0, message);
            }

            HasMoreMessages = olderMessages.Count == PageSize;

            Status = $"Loaded {olderMessages.Count} older messages";
        }
        catch (Exception ex)
        {
            if (IsCurrentConversation(conversation.ConversationId, version))
                Status = $"History loading failed: {ex.Message}";
        }
        finally
        {
            // Do not overwrite the loading state of another conversation.
            if (IsCurrentConversation(conversation.ConversationId, version))
                IsLoadingHistory = false;
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
            await _signalR.SendMessageAsync(
                conversation.ConversationId,
                content);

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

    private void OnMessageEdited(ChatMessage message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (SelectedConversation?.ConversationId == message.ConversationId)
            {
                var existing = Messages.FirstOrDefault(m => m.MessageId == message.MessageId);
                if (existing is not null)
                {
                    var index = Messages.IndexOf(existing);
                    Messages[index] = message;
                }
            }
        });
    }

    private void OnMessageDeleted(Guid conversationId, Guid messageId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (SelectedConversation?.ConversationId == conversationId)
            {
                var existing = Messages.FirstOrDefault(m => m.MessageId == messageId);
                if (existing is not null)
                {
                    Messages.Remove(existing);
                    LoadOlderMessagesCommand.NotifyCanExecuteChanged();
                }
            }
        });
    }

    private void AddMessage(ChatMessage message)
    {
        if (Messages.Any(m => m.MessageId == message.MessageId))
            return;

        Messages.Add(message);

        LoadOlderMessagesCommand.NotifyCanExecuteChanged();
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

        var version = _conversationVersion;

        try
        {
            await _signalR.JoinConversationAsync(conversation.ConversationId);

            if (!IsCurrentConversation(conversation.ConversationId, version))
                return;

            var history = await _messagesApi.GetMessagesAsync(
                conversation.ConversationId,
                limit: PageSize);

            if (!IsCurrentConversation(conversation.ConversationId, version))
                return;

            foreach (var message in history.OrderBy(m => m.CreatedAtUtc))
                AddMessage(message);

            Status = "Reconnected";
        }
        catch (Exception ex)
        {
            if (IsCurrentConversation(conversation.ConversationId, version))
                Status = $"Reconnect error: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        _signalR.MessageReceived -= OnMessageReceived;
        _signalR.MessageEdited -= OnMessageEdited;
        _signalR.MessageDeleted -= OnMessageDeleted;
        _signalR.Reconnected -= OnReconnected;

        await _signalR.DisposeAsync();
    }
}
