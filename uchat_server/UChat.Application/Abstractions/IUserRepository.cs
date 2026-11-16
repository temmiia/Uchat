using UChat.Domain.Entities;

namespace UChat.Application.Abstractions;

public interface IUserRepository
{
    Task<bool> ExistsByUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        User user,
        CancellationToken cancellationToken = default);
}