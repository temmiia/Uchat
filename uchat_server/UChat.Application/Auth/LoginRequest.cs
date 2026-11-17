namespace UChat.Application.Auth;

public sealed record LoginRequest(
    string Username,
    string Password);