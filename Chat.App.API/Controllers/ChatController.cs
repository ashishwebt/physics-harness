using System.Runtime.CompilerServices;
using System.Net.ServerSentEvents;
using Chat.App.API.Database;
using Chat.App.API.Services;
using ChatResponseModel = Chat.App.API.Models.ChatResponse;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Chat.App.API.Models;

namespace Chat.App.API.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{

    private readonly IAgentService _agentService;
    private readonly IConversationRepository _conversationRepository;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IAgentService agentService,
        IConversationRepository conversationRepository,
        ILogger<ChatController> logger)
    {
        _agentService = agentService;
        _conversationRepository = conversationRepository;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IResult> Chat(
        [FromBody] ChatRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new
            {
                detail = "Message cannot be empty"
            });
        }

        var conversationId = request.ConversationId;

        Database.Entities.Conversation conversation;

        if (conversationId is null)
        {
            conversation = await _conversationRepository.CreateAsync(ct: ct);
        }
        else
        {
            var existing =
                await _conversationRepository.GetAsync(
                    conversationId.Value,
                    ct);

            if (existing is null)
            {
                return Results.NotFound(new
                {
                    detail = "Conversation not found"
                });
            }

            conversation = existing;
        }

        var userMessage =
            new ChatMessage(
                ChatRole.User,
                request.Message.Trim());

        _logger.LogInformation(
            "Streaming chat response for conversation {ConversationId}",
            conversation.Id);

        Response.Headers["X-Conversation-Id"] =
            conversation.Id.ToString();

        return TypedResults.ServerSentEvents(
            StreamResponse(
                conversation.Id,
                userMessage,
                ct));
    }

    private async IAsyncEnumerable<SseItem<ChatResponseModel>> StreamResponse(
        Guid conversationId,
        ChatMessage userMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var accumulator = new System.Text.StringBuilder();

        await foreach (var update in _agentService.StreamAsync(
            conversationId.ToString(),
            userMessage,
            ct))
        {
            if (string.IsNullOrEmpty(update.Text))
            {
                continue;
            }

            accumulator.Append(update.Text);

            var payload = new ChatResponseModel(
                conversationId,
                new Message(
                    "assistant",
                    update.Text,
                    null));

            yield return new SseItem<ChatResponseModel>(
                payload,
                eventType: "message");
        }

        var finalPayload = new ChatResponseModel(
            conversationId,
            new Message(
                "assistant",
                accumulator.ToString(),
                null));

        yield return new SseItem<ChatResponseModel>(
            finalPayload,
            eventType: "done");
    }

    [HttpGet("{conversationsId:guid}")]
    public async Task<ActionResult<ConversationDetail>> Get(Guid conversationsId, CancellationToken ct)
    {
        _logger.LogInformation("Retrieving chat conversation {ConversationId}", conversationsId);

        var conversation = await _conversationRepository.GetAsync(conversationsId, ct);
        if (conversation is null)
        {
            _logger.LogWarning("Cchat conversation {ConversationId} was not found", conversationsId);
            return NotFound(new { detail = "Conversation not found" });
        }

        var history = await _agentService.GetHistoryAsync(
            conversationsId.ToString(),
            ct);

        var messages = history
            .Select(msg => new Message(
                msg.Role.ToString(),
                msg.Text ?? string.Empty,
                null))
            .ToList();

        var result = new ConversationDetail(
            conversation.Id,
            conversation.Title,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            messages);

        _logger.LogInformation("Retrieved conversation {ConversationId}", conversationsId);
        return Ok(result);
    }

}