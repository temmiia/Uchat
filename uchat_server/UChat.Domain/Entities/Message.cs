namespace UChat.Domain.Entities;

// Message entity in a conversation
public class Message
{
    // Parameterless constructor for EF Core
    private Message()
    {
    }

    public Message(
        Guid conversationId,
        string senderAccountId,
        string content,
        string? clientMessageId = null,
        Guid? messageId = null,
        DateTime? createdAtUtc = null)
    {
        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException("ConversationId cannot be empty.", nameof(conversationId));
        }

        if (string.IsNullOrWhiteSpace(senderAccountId))
        {
            throw new ArgumentException("SenderAccountId cannot be null or whitespace.", nameof(senderAccountId));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Message content cannot be empty or whitespace.", nameof(content));
        }

        MessageId = messageId ?? Guid.NewGuid();
        ConversationId = conversationId;
        SenderAccountId = senderAccountId;
        Content = content;
        ClientMessageId = clientMessageId;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        IsDeleted = false;
    }

    public Guid MessageId { get; private init; } = Guid.NewGuid();

    public Guid ConversationId { get; set; }
    public string SenderAccountId { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    // Time when the message was originally created (by user / offline client)
    public DateTime CreatedAtUtc { get; private init; } = DateTime.UtcNow;

    // Time when the server received the message (distinguishes offline created time from delivery time)
    public DateTime? ServerReceivedAtUtc { get; set; }

    public DateTime? EditedAtUtc { get; set; }

    // Soft delete: flag + clearing message text
    public bool IsDeleted { get; set; } = false;

    // Client-supplied idempotency key to prevent duplication on network retries
    public string? ClientMessageId { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public User Sender { get; set; } = null!;

    // Edit message content
    public void EditContent(string newContent)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("Cannot edit a deleted message.");
        }

        if (string.IsNullOrWhiteSpace(newContent))
        {
            throw new ArgumentException("New content cannot be empty or whitespace.", nameof(newContent));
        }

        Content = newContent;
        EditedAtUtc = DateTime.UtcNow;
    }

    // Soft delete: set flag and wipe content
    public void MarkAsDeleted()
    {
        IsDeleted = true;
        Content = string.Empty;
        EditedAtUtc = DateTime.UtcNow;
    }
}
