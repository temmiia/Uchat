using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace UChat.Server.Services;

public sealed class AccountUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirstValue(
            JwtRegisteredClaimNames.Sub)
            ?? connection.User?.FindFirstValue(
                ClaimTypes.NameIdentifier);
    }
}
