public sealed class ChatMessageEntity
{
    public long Id { get; set; }

    public string ConversationId { get; set; } = null!;

    public int Sequence { get; set; }

    public string MessageJson { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}