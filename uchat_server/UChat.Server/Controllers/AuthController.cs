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
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        RegisterUserService registerService,
        LoginUserService loginService,
        JwtTokenService jwtTokenService,
        ILogger<AuthController> logger)
    {
        _registerService = registerService;
        _loginService = loginService;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
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

            _logger.LogInformation("New user registered: UserName={UserName}, AccountId={AccountId}", user.UserName, user.AccountId);

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
            _logger.LogWarning(
                "Security event: Failed login attempt for username '{UserName}' from IP {RemoteIp}",
                request.Username?.Trim(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return Unauthorized(new
            {
                error = "Invalid username or password."
            });
        }

        var token = _jwtTokenService.GenerateToken(user);
        _logger.LogInformation("User '{UserName}' (AccountId={AccountId}) logged in successfully", user.UserName, user.AccountId);

        return Ok(new LoginResponse(
            AccessToken: token,
            TokenType: "Bearer",
            ExpiresIn: 3600));
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var accountId = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(accountId))
        {
            _logger.LogWarning("Security event: /api/auth/me called with token missing sub/nameidentifier claim");
            return Unauthorized(new { error = "Unauthorized." });
        }

        var username = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.UniqueName)?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
            ?? User.FindFirst("unique_name")?.Value
            ?? User.Identity?.Name;

        return Ok(new
        {
            accountId,
            username
        });
    }
}
