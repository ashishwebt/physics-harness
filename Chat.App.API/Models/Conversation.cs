namespace Chat.App.API.Models;

public record Conversation(Guid Id, string Title, DateTime CreatedAt, DateTime UpdatedAt);