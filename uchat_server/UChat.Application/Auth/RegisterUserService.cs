using Microsoft.AspNetCore.Identity;
using UChat.Application.Abstractions;
using UChat.Domain.Entities;

namespace UChat.Application.Auth;

public sealed class RegisterUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher<User> _passwordHasher;

    public RegisterUserService(
        IUserRepository userRepository,
        IPasswordHasher<User> passwordHasher)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
    }

    public async Task<User> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
            throw new ArgumentException("Username is required.");

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new ArgumentException("Display name is required.");

        if (string.IsNullOrWhiteSpace(request.Password))
            throw new ArgumentException("Password is required.");

        string username = request.Username.Trim();
        string normalizedUsername = username.ToUpperInvariant();

        if (username.Length < 3 || username.Length > 32)
            throw new ArgumentException(
                "Username must contain between 3 and 32 characters.");

        bool usernameExists =
            await _userRepository.ExistsByUsernameAsync(
                normalizedUsername,
                cancellationToken);

        if (usernameExists)
            throw new InvalidOperationException(
                "Username is already taken.");

        var user = new User
        {
            Username = username,
            NormalizedUsername = normalizedUsername,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = string.Empty
        };

        user.PasswordHash =
            _passwordHasher.HashPassword(user, request.Password);

        await _userRepository.CreateAsync(
            user,
            cancellationToken);

        return user;
    }
}