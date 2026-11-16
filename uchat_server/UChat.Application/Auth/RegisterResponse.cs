
namespace UChat.Application.Auth;

public sealed record RegisterResponse(
    string AccountId,
    string Username,
    string DisplayName
);
