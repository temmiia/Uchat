
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using UChat.Domain.Entities;

namespace UChat.Server.Services;

public sealed class JwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateToken(User user)
    {
        var key = Environment.GetEnvironmentVariable("UCHAT_JWT_KEY")
            ?? Environment.GetEnvironmentVariable("UCHAT_JWT_SECRET")
            ?? _configuration["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(key))
        {
            key = "uchat_development_secret_key_at_least_32_bytes_long_12345";
        }

        var issuer = Environment.GetEnvironmentVariable("UCHAT_JWT_ISSUER")
            ?? _configuration["Jwt:Issuer"];

        if (string.IsNullOrWhiteSpace(issuer))
        {
            issuer = "UChat.Server";
        }

        var audience = Environment.GetEnvironmentVariable("UCHAT_JWT_AUDIENCE")
            ?? _configuration["Jwt:Audience"];

        if (string.IsNullOrWhiteSpace(audience))
        {
            audience = "UChat.Client";
        }

        var trimmedKey = key.Trim();
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(trimmedKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.AccountId),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
