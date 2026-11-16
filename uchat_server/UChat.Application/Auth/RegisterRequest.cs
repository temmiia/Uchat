namespace UChat.Application.Auth;

public sealed record RegisterRequest(
    string Username,
    string DisplayName,
    string Password
);