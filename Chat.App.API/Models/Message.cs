namespace Chat.App.API.Models;

public record Message(string Role, string Content, DateTime? CreatedAt);