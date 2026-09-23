using Microsoft.EntityFrameworkCore;

public sealed class ChatHistoryDbContext(DbContextOptions<ChatHistoryDbContext> options)
    : DbContext(options)
{
    public DbSet<ChatMessageEntity> Messages => Set<ChatMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatMessageEntity>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.ConversationId,
                x.Sequence
            });

            entity.Property(x => x.ConversationId)
                .IsRequired();

            entity.Property(x => x.MessageJson)
                .IsRequired();
        });
    }
}