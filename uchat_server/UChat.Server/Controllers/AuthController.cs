using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UChat.Application.Auth;
using UChat.Server.Services;

namespace UChat.Server.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly RegisterUserService _registerService;
    private readonly LoginUserService _loginService;
    private readonly JwtTokenService _jwtTokenService;

    public AuthController(
        RegisterUserService registerService,
        LoginUserService loginService,
        JwtTokenService jwtTokenService)
    {
        _registerService = registerService;
        _loginService = loginService;
        _jwtTokenService = jwtTokenService;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await _registerService.RegisterAsync(
                request, cancellationToken);

            var response = new RegisterResponse(
                user.AccountId,
                user.UserName,
                user.DisplayName);

            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
            when (ex.Message == "Username is already taken.")
        {
            return Conflict(new { error = "Username is already taken." });
        }
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _loginService.LoginAsync(
            request, cancellationToken);

        if (user is null)
        {
            return Unauthorized(new
            {
                error = "Invalid username or password."
            });
        }

        var token = _jwtTokenService.GenerateToken(user);

        return Ok(new LoginResponse(
            AccessToken: token,
            TokenType: "Bearer",
            ExpiresIn: 3600));
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var accountId = User.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(accountId))
        {
            return Unauthorized();
        }

        return Ok(new
        {
            accountId,
            username = User.Identity?.Name
        });
    }
}
