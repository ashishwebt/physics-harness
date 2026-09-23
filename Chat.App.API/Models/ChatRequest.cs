namespace Chat.App.API.Models;

public record ChatRequest(Guid? ConversationId, string Message);