namespace Chat.App.API.Models;

public record ChatResponse(Guid ConversationId, Message AssistantMessage);