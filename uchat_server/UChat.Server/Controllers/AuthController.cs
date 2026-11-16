
using Microsoft.AspNetCore.Mvc;
using UChat.Application.Auth;

namespace UChat.Server.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly RegisterUserService _registerService;

    public AuthController(RegisterUserService registerService)
    {
        _registerService = registerService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await _registerService.RegisterAsync(
                request,
                cancellationToken);

            var response = new RegisterResponse(
                user.AccountId,
                user.UserName,
                user.DisplayName);

            return StatusCode(
                StatusCodes.Status201Created,
                response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (InvalidOperationException ex)
            when (ex.Message == "Username is already taken.")
        {
            return Conflict(new
            {
                error = "Username is already taken."
            });
        }
    }
}
