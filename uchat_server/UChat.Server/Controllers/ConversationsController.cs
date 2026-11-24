using UChat.Server.Services;
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
    private readonly ChatEventPublisher _events;

    public ConversationsController(
        AppDbContext db,
        ChatEventPublisher events)
    {
        _db = db;
        _events = events;
    }

    private string? CurrentAccountId =>
        User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public sealed record CreateDirectRequest(
        string TargetUsername);

    public sealed record CreateGroupRequest(
        string Title,
        string[] MemberUsernames);

    public sealed record ConversationResponse(
        Guid ConversationId,
        string Type,
        string DisplayName,
        string? TargetUsername,
        string? MyRole = null);

    [HttpGet]
    public async Task<IActionResult> GetMyConversations(
        CancellationToken cancellationToken)
    {
        var accountId = CurrentAccountId;

        if (string.IsNullOrWhiteSpace(accountId))
            return Unauthorized();

        var conversations = await _db.Conversations
            .AsNoTracking()
            .Where(c => c.Members.Any(
                m => m.AccountId == accountId))
            .Include(c => c.Members)
                .ThenInclude(m => m.User)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var result = conversations
            .Select(c => ToResponse(c, accountId))
            .ToList();

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
            return BadRequest(new
            {
                error = "Username is required."
            });

        var username = request.TargetUsername.Trim();

        var target = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.UserName.ToUpper() == username.ToUpper(),
                cancellationToken);

        if (target is null)
            return NotFound(new
            {
                error = "User not found."
            });

        if (target.AccountId == accountId)
            return BadRequest(new
            {
                error = "Cannot chat with yourself."
            });

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        var existing = await _db.Conversations
            .AsNoTracking()
            .Where(c => c.Type == ConversationType.Direct)
            .Where(c => c.Members.Any(
                m => m.AccountId == accountId))
            .Where(c => c.Members.Any(
                m => m.AccountId == target.AccountId))
            .Where(c => c.Members.Count() == 2)
            .Select(c => c.ConversationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != Guid.Empty)
        {
            return Ok(new ConversationResponse(
                existing,
                "Direct",
                target.DisplayName,
                target.UserName,
                MemberRole.Member.ToString()));
        }

        var conversation =
            new Conversation(ConversationType.Direct);

        _db.Conversations.Add(conversation);

        _db.Members.Add(
            new ConversationMember(
                conversation.ConversationId,
                accountId));

        _db.Members.Add(
            new ConversationMember(
                conversation.ConversationId,
                target.AccountId));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _events.PublishConversationCreatedAsync(
            accountId,
            target.AccountId,
            conversation.ConversationId);

        return StatusCode(
            StatusCodes.Status201Created,
            new ConversationResponse(
                conversation.ConversationId,
                "Direct",
                target.DisplayName,
                target.UserName,
                MemberRole.Member.ToString()));
    }

    [HttpPost("group")]
    public async Task<IActionResult> CreateGroup(
        [FromBody] CreateGroupRequest request,
        CancellationToken cancellationToken)
    {
        var accountId = CurrentAccountId;

        if (string.IsNullOrWhiteSpace(accountId))
            return Unauthorized();

        var title = request.Title?.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new
            {
                error = "Group title is required."
            });
        }

        if (title.Length > 128)
        {
            return BadRequest(new
            {
                error = "Group title cannot exceed 128 characters."
            });
        }

        var owner = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.AccountId == accountId,
                cancellationToken);

        if (owner is null)
            return Unauthorized();

        var usernames = (request.MemberUsernames
                ?? Array.Empty<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(u => !string.Equals(
                u,
                owner.UserName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (usernames.Length == 0)
        {
            return BadRequest(new
            {
                error = "Add at least one group member."
            });
        }

        if (usernames.Length > 99)
        {
            return BadRequest(new
            {
                error = "A group cannot exceed 100 members."
            });
        }

        var normalizedUsernames = usernames
            .Select(u => u.ToUpperInvariant())
            .ToArray();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => normalizedUsernames.Contains(
                u.UserName.ToUpper()))
            .ToListAsync(cancellationToken);

        var foundUsernames = users
            .Select(u => u.UserName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingUsernames = usernames
            .Where(u => !foundUsernames.Contains(u))
            .ToArray();

        if (missingUsernames.Length > 0)
        {
            return NotFound(new
            {
                error = "Some users were not found.",
                usernames = missingUsernames
            });
        }

        var memberIds = users
            .Select(u => u.AccountId)
            .Where(id => id != accountId)
            .Distinct()
            .ToList();

        if (memberIds.Count == 0)
        {
            return BadRequest(new
            {
                error = "Add at least one group member."
            });
        }

        var conversation = new Conversation(
            ConversationType.Group,
            title);

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        _db.Conversations.Add(conversation);

        // The authenticated creator becomes the owner.
        _db.Members.Add(
            new ConversationMember(
                conversation.ConversationId,
                accountId,
                MemberRole.Owner));

        // Invited users become regular members.
        foreach (var memberId in memberIds)
        {
            _db.Members.Add(
                new ConversationMember(
                    conversation.ConversationId,
                    memberId,
                    MemberRole.Member));
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Notify every participant after saving the group.
        memberIds.Add(accountId);

        await _events.PublishConversationActivityAsync(
            conversation.ConversationId,
            memberIds);

        return StatusCode(
            StatusCodes.Status201Created,
            new ConversationResponse(
                conversation.ConversationId,
                "Group",
                title,
                null,
                MemberRole.Owner.ToString()));
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
                c => c.ConversationId == conversationId
                     && c.Members.Any(
                         m => m.AccountId == accountId),
                cancellationToken);

        if (conversation is null)
            return NotFound();

        return Ok(ToResponse(conversation, accountId));
    }

    private static ConversationResponse ToResponse(
        Conversation conversation,
        string accountId)
    {
        var otherUser = conversation.Members
            .FirstOrDefault(
                m => m.AccountId != accountId)?.User;

        var membership = conversation.Members
            .FirstOrDefault(
                m => m.AccountId == accountId);

        return new ConversationResponse(
            conversation.ConversationId,
            conversation.Type.ToString(),
            conversation.Type == ConversationType.Direct
                ? otherUser?.DisplayName ?? "Direct chat"
                : conversation.Title ?? "Group chat",
            conversation.Type == ConversationType.Direct
                ? otherUser?.UserName
                : null,
            membership?.Role.ToString());
    }
}
