public sealed class ChatMemoryFileEntity
{
    public long Id { get; set; }

    public string ConversationId { get; set; } = null!;

    public string NamespaceKey { get; set; } = "memories";

    public string Path { get; set; } = null!;

    public byte[] Content { get; set; } = null!;

    public DateTimeOffset UpdatedAt { get; set; }
}