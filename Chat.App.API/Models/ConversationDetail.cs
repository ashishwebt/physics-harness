namespace Chat.App.API.Models;

public record ConversationDetail(Guid Id, string Title, DateTime CreatedAt, DateTime UpdatedAt, List<Message> Messages)
{
    public int MessageCount => Messages.Count;
}