using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UChat.App.Models;

public sealed partial class ChatPreview : ObservableObject
{
    public ChatPreview(string name, string avatarInitials, string lastMessage, string time)
    {
        Name = name;
        AvatarInitials = avatarInitials;
        LastMessage = lastMessage;
        Time = time;
    }

    public string Name { get; }
    public string AvatarInitials { get; }
    public ObservableCollection<ChatMessage> Messages { get; } = [];

    [ObservableProperty]
    private string lastMessage = string.Empty;

    [ObservableProperty]
    private string time = string.Empty;
}
