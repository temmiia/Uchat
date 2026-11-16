namespace UChat.Domain.Entities;

// Модель сообщения в чате
public class Message
{
    // Пустой конструктор для EF Core
    private Message()
    {
    }

    public Message(
        Guid conversationId,
        string senderAccountId,
        string content,
        string? clientMessageId = null,
        Guid? messageId = null)
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
        CreatedAtUtc = DateTime.UtcNow;
        IsDeleted = false;
    }

    public Guid MessageId { get; private init; } = Guid.NewGuid();

    public Guid ConversationId { get; set; }
    public string SenderAccountId { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private init; } = DateTime.UtcNow;
    public DateTime? EditedAtUtc { get; set; }

    // Мягкое удаление (флаг + затирание текста)
    public bool IsDeleted { get; set; } = false;

    // Идемпотентность: клиентский ID для защиты от дублирования при сбоях сети
    public string? ClientMessageId { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public User Sender { get; set; } = null!;

    // Редактирование текста сообщения
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

    // Мягкое удаление: помечаем флаг и затираем текст
    public void MarkAsDeleted()
    {
        IsDeleted = true;
        Content = string.Empty;
        EditedAtUtc = DateTime.UtcNow;
    }
}
