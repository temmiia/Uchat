
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
    public ObservableCollection<ChatConversation> FilteredConversations { get; } = new();
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadOlderMessagesCommand))]
    private ChatConversation? selectedConversation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateChatCommand))]
    private string newChatUsername = "";

    [ObservableProperty]
    private bool isNewChatInputVisible;

    [ObservableProperty]
    private string messageText = "";

    [ObservableProperty]
    private ChatMessage? editingMessage;

    [ObservableProperty]
    private bool isEditingMessage;

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

            ApplyConversationFilter();
        }
        catch (Exception ex)
        {
            Status = $"Load error: {ex.Message}";
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyConversationFilter();
    }

    private void ApplyConversationFilter()
    {
        var query = SearchText.Trim();
        var matches = Conversations
            .Where(conversation =>
                query.Length == 0
                || conversation.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (conversation.TargetUsername?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        FilteredConversations.Clear();
        foreach (var conversation in matches)
            FilteredConversations.Add(conversation);
    }

    [RelayCommand]
    private void ToggleNewChatInput()
    {
        IsNewChatInputVisible = !IsNewChatInputVisible;
    }

    [RelayCommand(CanExecute = nameof(CanCreateChat))]
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
            IsNewChatInputVisible = false;

            SelectedConversation = Conversations.FirstOrDefault(
                c => c.ConversationId == conversation.ConversationId);
        }
        catch (Exception ex)
        {
            Status = $"Create chat error: {ex.Message}";
        }
    }

    private bool CanCreateChat()
    {
        return !string.IsNullOrWhiteSpace(NewChatUsername);
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
            if (EditingMessage is not null)
            {
                await _signalR.EditMessageAsync(
                    conversation.ConversationId,
                    EditingMessage.MessageId,
                    content);

                CancelEdit();
                return;
            }

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

    [RelayCommand]
    private void EditMessage(ChatMessage message)
    {
        if (!message.IsOwnMessage)
            return;

        EditingMessage = message;
        IsEditingMessage = true;
        MessageText = message.Content;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingMessage = null;
        IsEditingMessage = false;
        MessageText = "";
    }

    [RelayCommand]
    private async Task DeleteMessageAsync(ChatMessage message)
    {
        var conversation = SelectedConversation;
        if (conversation is null || !message.IsOwnMessage)
            return;

        try
        {
            await _signalR.DeleteMessageAsync(
                conversation.ConversationId,
                message.MessageId);
        }
        catch (Exception ex)
        {
            Status = $"Delete error: {ex.Message}";
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
                    message.IsOwnMessage =
                        string.Equals(
                            message.SenderAccountId,
                            AuthSession.AccountId,
                            StringComparison.Ordinal);

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

        message.IsOwnMessage =
            string.Equals(
                message.SenderAccountId,
                AuthSession.AccountId,
                StringComparison.Ordinal);

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
