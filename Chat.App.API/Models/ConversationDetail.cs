namespace Chat.App.API.Models;

public record ConversationMemoryFile(string Path, string Content, long Size, DateTime UpdatedAt);

public record ConversationDetail(Guid Id, string Title, DateTime CreatedAt, DateTime UpdatedAt, List<Message> Messages, List<ConversationMemoryFile> Files)
{
    public int MessageCount => Messages.Count;
    public int FileCount => Files.Count;
}