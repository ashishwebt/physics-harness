using Chat.App.API.Database;
using Microsoft.AspNetCore.Mvc;
using Chat.App.API.Models;

namespace Chat.App.API.Controllers;

[ApiController]
[Route("api/conversations")]
public class ConversationsController : ControllerBase
{
    private readonly IConversationRepository _conversationRepository;
    private readonly ILogger<ConversationsController> _logger;

    public ConversationsController(
        IConversationRepository conversationRepository,
        ILogger<ConversationsController> logger)
    {
        _conversationRepository = conversationRepository;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<Conversation>>> List([FromQuery] int skip = 0, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        _logger.LogInformation("Listing conversations with skip {Skip} and limit {Limit}", skip, limit);

        var conversations = await _conversationRepository.ListAsync(skip, limit, ct);
        var result = conversations.Select(c => new Conversation(c.Id, c.Title, c.CreatedAt, c.UpdatedAt)).ToList();

        _logger.LogInformation("Retrieved {Count} conversations", result.Count);
        return Ok(result);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<Conversation>> Rename(Guid id, [FromBody] RenameConversationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            _logger.LogWarning("Rename request rejected for conversation {ConversationId}: title was empty", id);
            return BadRequest(new { detail = "Title cannot be empty" });
        }

        _logger.LogInformation("Renaming conversation {ConversationId} to {Title}", id, request.Title);

        var conversation = await _conversationRepository.RenameAsync(id, request.Title, ct);
        if (conversation is null)
        {
            _logger.LogWarning("Unable to rename missing conversation {ConversationId}", id);
            return NotFound(new { detail = "Conversation not found" });
        }

        _logger.LogInformation("Renamed conversation {ConversationId}", id);
        return Ok(new Conversation(conversation.Id, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("Deleting conversation {ConversationId}", id);

        var deleted = await _conversationRepository.DeleteAsync(id, ct);
        if (!deleted)
        {
            _logger.LogWarning("Delete failed because conversation {ConversationId} was not found", id);
            return NotFound(new { detail = "Conversation not found" });
        }

        _logger.LogInformation("Deleted conversation {ConversationId}", id);
        return Ok(new { deleted = true });
    }
}