
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UChat.Domain.Entities;
using UChat.Domain.Enums;
using UChat.Infrastructure.Data;

namespace UChat.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/conversations")]
public sealed class ConversationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ConversationsController(AppDbContext db)
    {
        _db = db;
    }

    private string? CurrentAccountId =>
        User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public sealed record CreateDirectRequest(string TargetUsername);

    public sealed record ConversationResponse(
        Guid ConversationId,
        string Type,
        string DisplayName,
        string? TargetUsername);

    [HttpGet]
    public async Task<IActionResult> GetMyConversations(
        CancellationToken cancellationToken)
    {
        var accountId = CurrentAccountId;

        if (string.IsNullOrWhiteSpace(accountId))
            return Unauthorized();

        var conversations = await _db.Conversations
            .AsNoTracking()
            .Where(c => c.Members.Any(m => m.AccountId == accountId))
            .Include(c => c.Members)
                .ThenInclude(m => m.User)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var result = conversations.Select(c =>
        {
            var otherUser = c.Members
                .FirstOrDefault(m => m.AccountId != accountId)?.User;

            return new ConversationResponse(
                c.ConversationId,
                c.Type.ToString(),
                c.Type == ConversationType.Direct
                    ? otherUser?.DisplayName ?? "Direct chat"
                    : c.Title ?? "Group chat",
                c.Type == ConversationType.Direct
                    ? otherUser?.UserName
                    : null);
        });

        return Ok(result);
    }

    [HttpPost("direct")]
    public async Task<IActionResult> CreateDirect(
        [FromBody] CreateDirectRequest request,
        CancellationToken cancellationToken)
    {
        var accountId = CurrentAccountId;

        if (string.IsNullOrWhiteSpace(accountId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.TargetUsername))
            return BadRequest(new { error = "Username is required." });

        var username = request.TargetUsername.Trim();

        var target = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.UserName.ToUpper() == username.ToUpper(),
                cancellationToken);

        if (target is null)
            return NotFound(new { error = "User not found." });

        if (target.AccountId == accountId)
            return BadRequest(new { error = "Cannot chat with yourself." });

        var existing = await _db.Conversations
            .AsNoTracking()
            .Where(c => c.Type == ConversationType.Direct)
            .Where(c => c.Members.Any(m => m.AccountId == accountId))
            .Where(c => c.Members.Any(m => m.AccountId == target.AccountId))
            .Where(c => c.Members.Count() == 2)
            .Select(c => c.ConversationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != Guid.Empty)
        {
            return Ok(new ConversationResponse(
                existing,
                "Direct",
                target.DisplayName,
                target.UserName));
        }

        var conversation = new Conversation(ConversationType.Direct);

        _db.Conversations.Add(conversation);

        _db.Members.Add(
            new ConversationMember(conversation.ConversationId, accountId));

        _db.Members.Add(
            new ConversationMember(conversation.ConversationId, target.AccountId));

        await _db.SaveChangesAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new ConversationResponse(
                conversation.ConversationId,
                "Direct",
                target.DisplayName,
                target.UserName));
    }

    [HttpGet("{conversationId:guid}")]
    public async Task<IActionResult> GetConversation(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var accountId = CurrentAccountId;

        if (string.IsNullOrWhiteSpace(accountId))
            return Unauthorized();

        var conversation = await _db.Conversations
            .AsNoTracking()
            .Include(c => c.Members)
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(
                c => c.ConversationId == conversationId &&
                     c.Members.Any(m => m.AccountId == accountId),
                cancellationToken);

        if (conversation is null)
            return NotFound();

        var otherUser = conversation.Members
            .FirstOrDefault(m => m.AccountId != accountId)?.User;

        return Ok(new ConversationResponse(
            conversation.ConversationId,
            conversation.Type.ToString(),
            conversation.Type == ConversationType.Direct
                ? otherUser?.DisplayName ?? "Direct chat"
                : conversation.Title ?? "Group chat",
            conversation.Type == ConversationType.Direct
                ? otherUser?.UserName
                : null));
    }
}
