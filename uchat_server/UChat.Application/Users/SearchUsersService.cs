using UChat.Application.Abstractions;

namespace UChat.Application.Users;

public sealed class SearchUsersService
{
    private readonly IUserRepository _userRepository;

    public SearchUsersService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<IReadOnlyList<UserSearchResult>> SearchAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<UserSearchResult>();
        }

        var cleanQuery = query.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            return Array.Empty<UserSearchResult>();
        }

        var users = await _userRepository.SearchByUsernameAsync(cleanQuery, limit, cancellationToken);

        return users.Select(user => new UserSearchResult(
            user.AccountId,
            user.UserName,
            user.DisplayName,
            user.AvatarUrl,
            user.Bio)).ToList();
    }
}
