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
    private readonly System.Threading.SemaphoreSlim _refreshLock = new(1);
    private readonly System.Collections.Generic.HashSet<Guid> _deleted = new();
    private readonly System.Collections.Generic.Dictionary<Guid, ChatMessage> _edits = new();

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
    private bool isGroupDialogOpen;

    [ObservableProperty]
    private string groupTitle = "";

    [ObservableProperty]
    private string groupMemberUsername = "";

    [ObservableProperty]
    private string groupError = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditGroup))]
    private bool isCreatingGroup;

    public bool CanEditGroup => !IsCreatingGroup;

    public ObservableCollection<string> GroupMembers { get; } = new();

    [ObservableProperty]
    private string messageText = "";

    [ObservableProperty]
    private ChatMessage? editingMessage;

    [ObservableProperty]
    private bool isEditingMessage;
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(SendButtonText))]
    private ChatMessage? editingMessage;
    public bool IsEditing => EditingMessage is not null;
    public string SendButtonText => IsEditing ? "Save" : "Send";

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
        _signalR.ConversationCreated += OnConversationCreated;
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
        await _refreshLock.WaitAsync();
        try
        {
            var conversations = await _conversationsApi.GetConversationsAsync();
            foreach (var conversation in conversations)
                Conversations.Add(conversation);

            ApplyConversationFilter();
            {
                if (!Conversations.Any(c => c.ConversationId == conversation.ConversationId))
                    Conversations.Add(conversation);
            }
        }
        catch (Exception ex) { Status = $"Load error: {ex.Message}"; }
        finally { _refreshLock.Release(); }
    }

    private void OnConversationCreated()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            Console.WriteLine("[Chat] Refreshing conversations");

            await RefreshConversationsAsync();

            Console.WriteLine(
                $"[Chat] Conversations count: {Conversations.Count}");
        });
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
    [RelayCommand]
    private void OpenGroupDialog()
    {
        if (IsCreatingGroup)
            return;

        GroupTitle = "";
        GroupMemberUsername = "";
        GroupError = "";
        GroupMembers.Clear();

        IsGroupDialogOpen = true;
    }

    [RelayCommand]
    private void CloseGroupDialog()
    {
        if (IsCreatingGroup)
            return;

        IsGroupDialogOpen = false;

        GroupTitle = "";
        GroupMemberUsername = "";
        GroupError = "";
        GroupMembers.Clear();
    }

    [RelayCommand]
    private void AddGroupMember()
    {
        if (IsCreatingGroup)
            return;

        var username = GroupMemberUsername
            .Trim()
            .TrimStart('@')
            .Trim();

        GroupError = "";

        if (string.IsNullOrWhiteSpace(username))
        {
            GroupError = "Enter a username.";
            return;
        }

        if (GroupMembers.Any(u =>
            string.Equals(
                u,
                username,
                StringComparison.OrdinalIgnoreCase)))
        {
            GroupError = "This user is already added.";
            return;
        }

        if (GroupMembers.Count >= 99)
        {
            GroupError = "Maximum 99 invited members.";
            return;
        }

        GroupMembers.Add(username);

        GroupMemberUsername = "";
    }

    [RelayCommand]
    private void RemoveGroupMember(string? username)
    {
        if (IsCreatingGroup || username is null)
            return;

        GroupMembers.Remove(username);

        GroupError = "";
    }

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        if (IsCreatingGroup)
            return;

        GroupError = "";

        var title = GroupTitle.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            GroupError = "Enter a group name.";
            return;
        }

        if (title.Length > 128)
        {
            GroupError = "Group name is too long.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(GroupMemberUsername))
        {
            GroupError = "Press Add to include the entered username.";
            return;
        }

        if (GroupMembers.Count == 0)
        {
            GroupError = "Add at least one member.";
            return;
        }

        IsCreatingGroup = true;

        try
        {
            var conversation =
                await _conversationsApi.CreateGroupAsync(
                    title,
                    GroupMembers.ToArray());

            await RefreshConversationsAsync();

            var existing = Conversations.FirstOrDefault(
                c => c.ConversationId == conversation.ConversationId);

            if (existing is null)
            {
                Conversations.Insert(0, conversation);
                existing = conversation;
            }

            SelectedConversation = existing;

            IsGroupDialogOpen = false;

            GroupTitle = "";
            GroupMemberUsername = "";
            GroupMembers.Clear();

            Status = $"Group created: {conversation.DisplayName}";
        }
        catch (Exception ex)
        {
            GroupError = ex.Message;
        }
        finally
        {
            IsCreatingGroup = false;
        }
    }

    partial void OnSelectedConversationChanged(ChatConversation? value)
    {
        var version = ++_conversationVersion;
        CancelEdit();
        _deleted.Clear();
        _edits.Clear();

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
                if (existingIds.Add(message.MessageId) && !_deleted.Contains(message.MessageId))
                    Messages.Insert(0, Prepare(message));
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
    private void StartEdit(ChatMessage? message)
    {
        if (message is null || !message.IsOwnMessage || SelectedConversation?.ConversationId != message.ConversationId) return;
        EditingMessage = message;
        MessageText = message.Content;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        if (EditingMessage is null) return;
        EditingMessage = null;
        MessageText = "";
    }

    [RelayCommand]
    private async Task DeleteMessageAsync(ChatMessage? message)
    {
        if (message is null || !message.IsOwnMessage || SelectedConversation?.ConversationId != message.ConversationId) return;
        try { await _messagesApi.DeleteMessageAsync(message.ConversationId, message.MessageId); }
        catch (Exception ex) { Status = $"Delete error: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var conversation = SelectedConversation;
        var content = MessageText.Trim();
        var editing = EditingMessage;
        var version = _conversationVersion;
        if (conversation is null || content.Length == 0 || content.Length > 4000) return;
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
            if (editing is not null)
            {
                if (!editing.IsOwnMessage || editing.ConversationId != conversation.ConversationId) return;
                await _messagesApi.EditMessageAsync(conversation.ConversationId, editing.MessageId, content);
            }
            else await _signalR.SendMessageAsync(conversation.ConversationId, content);
            if (IsCurrentConversation(conversation.ConversationId, version) && EditingMessage == editing && MessageText.Trim() == content)
            {
                EditingMessage = null;
                MessageText = "";
            }
        }
        catch (Exception ex) { Status = $"Send error: {ex.Message}"; }
    }

    private ChatMessage Prepare(ChatMessage message)
    {
        if (_edits.TryGetValue(
                message.MessageId,
                out var edited)
            && edited.EditedAtUtc >=
                (message.EditedAtUtc ?? DateTime.MinValue))
        {
            message = edited;
        }

        if (message.IsOwnMessage)
        {
            message.SenderLabel = "You";
        }
        else if (!string.IsNullOrWhiteSpace(
                     message.SenderDisplayName))
        {
            message.SenderLabel =
                message.SenderDisplayName;
        }
        else if (SelectedConversation?.Type == "Direct")
        {
            message.SenderLabel =
                SelectedConversation.DisplayName;
        }
        else
        {
            message.SenderLabel = message.SenderAccountId;
        }

        return message;
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
    private void OnMessageEdited(ChatMessage message) => Dispatcher.UIThread.Post(() =>
    {
        if (SelectedConversation?.ConversationId != message.ConversationId || _deleted.Contains(message.MessageId)) return;
        if (_edits.TryGetValue(message.MessageId, out var previous) && previous.EditedAtUtc > message.EditedAtUtc) return;
        _edits[message.MessageId] = message;
        var existing = Messages.FirstOrDefault(m => m.MessageId == message.MessageId);
        if (existing is not null && (existing.EditedAtUtc ?? DateTime.MinValue) <= message.EditedAtUtc)
            Messages[Messages.IndexOf(existing)] = Prepare(message);
    });

    private void OnMessageDeleted(MessageDeletedEvent message) => Dispatcher.UIThread.Post(() =>
    {
        if (SelectedConversation?.ConversationId != message.ConversationId) return;
        _deleted.Add(message.MessageId);
        _edits.Remove(message.MessageId);
        var existing = Messages.FirstOrDefault(m => m.MessageId == message.MessageId);
        if (existing is not null) Messages.Remove(existing);
        if (EditingMessage?.MessageId == message.MessageId) CancelEdit();
        LoadOlderMessagesCommand.NotifyCanExecuteChanged();
    });

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
        if (_deleted.Contains(message.MessageId) || Messages.Any(m => m.MessageId == message.MessageId))
            return;

        message.IsOwnMessage =
            string.Equals(
                message.SenderAccountId,
                AuthSession.AccountId,
                StringComparison.Ordinal);

        Messages.Add(message);
        Messages.Add(Prepare(message));

        LoadOlderMessagesCommand.NotifyCanExecuteChanged();
    }

    private void OnReconnected()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _ = RefreshConversationsAsync();
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

            Messages.Clear();
            HasMoreMessages = history.Count == PageSize;
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
        _signalR.ConversationCreated -= OnConversationCreated;
        _signalR.Reconnected -= OnReconnected;

        await _signalR.DisposeAsync();
    }
}
