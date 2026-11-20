namespace UChat.Application.Users;

public sealed record UserSearchResult(
    string AccountId,
    string UserName,
    string DisplayName,
    string? AvatarUrl,
    string? Bio);
