namespace UChat.Application.Auth;

public sealed record RegisterResponse(
    Guid Id,
    string Username,
    string DisplayName
);
