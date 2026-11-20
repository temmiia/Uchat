using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UChat.Application.Users;

namespace UChat.Server.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly SearchUsersService _searchUsersService;

    public UsersController(SearchUsersService searchUsersService)
    {
        _searchUsersService = searchUsersService;
    }

    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<UserSearchResult>>> Search(
        [FromQuery] string query,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit is <= 0 or > 100)
        {
            limit = 20;
        }

        var results = await _searchUsersService.SearchAsync(query, limit, cancellationToken);
        return Ok(results);
    }
}
