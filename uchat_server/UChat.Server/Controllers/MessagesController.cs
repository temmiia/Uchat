using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UChat.Application.Messages;
using UChat.Infrastructure.Data;
using UChat.Server.Services;

namespace UChat.Server.Controllers;

[ApiController]
[Route("api/conversations/{conversationId:guid}/messages")]
[Authorize]
public sealed class MessagesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly MessageService _messageService;
    private readonly ChatEventPublisher _eventPublisher;

    public MessagesController(
        AppDbContext dbContext,
        MessageService messageService,
        ChatEventPublisher eventPublisher)
    {
        _dbContext = dbContext;
        _messageService = messageService;
        _eventPublisher = eventPublisher;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MessageResponse>>> GetMessages(
        [FromRoute] Guid conversationId,
        [FromQuery] int limit = 50,
        [FromQuery] DateTime? before = null,
        [FromQuery] DateTime? after = null,
        CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();

        if (string.IsNullOrWhiteSpace(accountId))
        {
            return Unauthorized();
        }

        var isMember = await CheckMembershipAsync(
            conversationId, accountId, cancellationToken);

        if (!isMember)
        {
            return Forbid();
        }

        var messages = await _messageService.GetMessagesAsync(
            conversationId,
            limit,
            before,
            after,
            cancellationToken);

        return Ok(messages);
    }

    [HttpPost]
    public async Task<ActionResult<MessageResponse>> SendMessage(
        [FromRoute] Guid conversationId,
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();

        if (string.IsNullOrWhiteSpace(accountId))
        {
            return Unauthorized();
        }

        var isMember = await CheckMembershipAsync(
            conversationId, accountId, cancellationToken);

        if (!isMember)
        {
            return Forbid();
        }

        try
        {
            var (response, isDuplicate) =
                await _messageService.SaveMessageAsync(
                    conversationId,
                    accountId,
                    request.Content,
                    request.ClientMessageId,
                    request.CreatedAtUtc,
                    cancellationToken);

            if (!isDuplicate)
            {
                var messageEntity = await _dbContext.Messages
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        m => m.MessageId == response.MessageId,
                        cancellationToken);

                if (messageEntity is not null)
                {
                    await _eventPublisher.PublishMessageCreatedAsync(
                        messageEntity,
                        cancellationToken);
                }

                return StatusCode(
                    StatusCodes.Status201Created, response);
            }

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("{messageId:guid}")]
    public async Task<IActionResult> EditMessage(
        [FromRoute] Guid conversationId,
        [FromRoute] Guid messageId,
        [FromBody] EditMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();

        if (string.IsNullOrWhiteSpace(accountId))
        {
            return Unauthorized();
        }

        if (!await CheckMembershipAsync(
            conversationId, accountId, cancellationToken))
        {
            return Forbid();
        }

        try
        {
            var message = await _messageService.EditMessageAsync(
                conversationId,
                messageId,
                accountId,
                request.Content,
                cancellationToken);

            await _eventPublisher.PublishMessageEditedAsync(
                message,
                cancellationToken);

            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{messageId:guid}")]
    public async Task<IActionResult> DeleteMessage(
        [FromRoute] Guid conversationId,
        [FromRoute] Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var accountId = GetCurrentAccountId();

        if (string.IsNullOrWhiteSpace(accountId))
        {
            return Unauthorized();
        }

        if (!await CheckMembershipAsync(
            conversationId, accountId, cancellationToken))
        {
            return Forbid();
        }

        try
        {
            await _messageService.DeleteMessageAsync(
                conversationId,
                messageId,
                accountId,
                cancellationToken);

            await _eventPublisher.PublishMessageDeletedAsync(
                conversationId,
                messageId,
                cancellationToken);

            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private string? GetCurrentAccountId() =>
        User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private Task<bool> CheckMembershipAsync(
        Guid conversationId,
        string accountId,
        CancellationToken cancellationToken) =>
        _dbContext.Members
            .AsNoTracking()
            .AnyAsync(
                m => m.ConversationId == conversationId &&
                     m.AccountId == accountId,
                cancellationToken);
}
