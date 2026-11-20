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

    Task<User?> GetByUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> SearchByUsernameAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default);
}
