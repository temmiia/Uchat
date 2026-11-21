using Avalonia.Layout;

namespace UChat.App.Models;

public sealed class ChatMessage(string text, string time, bool isMine)
{
    public string Text { get; } = text;
    public string Time { get; } = time;
    public HorizontalAlignment Alignment { get; } =
        isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left;
}
