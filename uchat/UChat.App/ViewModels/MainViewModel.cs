using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UChat.App.Models;

namespace UChat.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public ObservableCollection<ChatPreview> VisibleChats { get; } = [];

    public MainViewModel()
    {
        allChats[0].Messages.Add(new ChatMessage("Шо ви пацики?", "14:16", false));
        allChats[0].Messages.Add(new ChatMessage("Та ниче, потихоньку", "14:18", true));
        allChats[0].Messages.Add(new ChatMessage("зачет!", "14:20", false));
        allChats[1].Messages.Add(new ChatMessage("Фиксани уже наконец-то", "13:45", false));
        allChats[2].Messages.Add(new ChatMessage("Когда в дотку?", "Yesterday", false));
        allChats[3].Messages.Add(new ChatMessage("Читай PDF!", "Mon", false));

        foreach (var chat in allChats)
        {
            VisibleChats.Add(chat);
        }

        SelectedChat = VisibleChats.FirstOrDefault();
    }

    [ObservableProperty]
    public partial ChatPreview? SelectedChat { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DraftText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        var selectedChat = SelectedChat;
        var matches = allChats
            .Where(chat => chat.Name.Contains(value.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .ToArray();

        VisibleChats.Clear();
        foreach (var chat in matches)
        {
            VisibleChats.Add(chat);
        }

        if (selectedChat is null || !matches.Contains(selectedChat))
        {
            SelectedChat = matches.FirstOrDefault();
        }
    }

    private readonly ChatPreview[] allChats =
    [
        new ChatPreview("Stasik Team", "PT", "Шо ви пацики?", "14:20"),
        new ChatPreview("Богдан", "AS", "Фиксани уже наконец-то", "13:45"),
        new ChatPreview("Кентик", "D", "Когда в дотку?", "Yesterday"),
        new ChatPreview("УР", "E", "Читай PDF!", "Mon")
    ];

    [RelayCommand]
    private void SendMessage()
    {
        var text = DraftText.Trim();
        if (text.Length == 0 || SelectedChat is null)
        {
            return;
        }

        var time = DateTime.Now.ToString("HH:mm");
        SelectedChat.Messages.Add(new ChatMessage(text, time, true));
        SelectedChat.LastMessage = text;
        SelectedChat.Time = time;
        DraftText = string.Empty;
    }
}
