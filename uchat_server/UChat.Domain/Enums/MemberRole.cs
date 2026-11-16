namespace UChat.Domain.Enums;

/// <summary>
/// Specifies the role and permission level of a member in a conversation.
/// </summary>
public enum MemberRole
{
    /// <summary>
    /// Creator and primary owner of the conversation with full permissions.
    /// </summary>
    Owner = 1,

    /// <summary>
    /// Administrator with permission to manage members and moderate messages.
    /// </summary>
    Admin = 2,

    /// <summary>
    /// Regular member who can read and send messages.
    /// </summary>
    Member = 3
}
