using Microsoft.AspNetCore.Identity;
using UChat.Application.Abstractions;
using UChat.Domain.Entities;

namespace UChat.Application.Auth;

public sealed class LoginUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher<User> _passwordHasher;

    public LoginUserService(
        IUserRepository userRepository,
        IPasswordHasher<User> passwordHasher)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
    }

    public async Task<User?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return null;
        }

        var normalizedUsername = request.Username.Trim().ToUpperInvariant();

        var user = await _userRepository.GetByUsernameAsync(
            normalizedUsername,
            cancellationToken);

        if (user is null)
            return null;

        var result = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        if (result == PasswordVerificationResult.Failed)
            return null;

        return user;
    }
}