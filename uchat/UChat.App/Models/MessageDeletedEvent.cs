using System;

namespace UChat.App.Models;

public sealed class MessageDeletedEvent
{
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
}
