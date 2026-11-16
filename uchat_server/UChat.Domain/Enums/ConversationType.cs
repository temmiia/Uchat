namespace UChat.Domain.Enums;

/// <summary>
/// Specifies the type of conversation.
/// </summary>
public enum ConversationType
{
    /// <summary>
    /// One-on-one direct chat between two users.
    /// </summary>
    Direct = 1,

    /// <summary>
    /// Multi-user group chat.
    /// </summary>
    Group = 2
}
