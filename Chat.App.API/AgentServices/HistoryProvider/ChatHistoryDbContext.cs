using Microsoft.EntityFrameworkCore;

public sealed class ChatHistoryDbContext(DbContextOptions<ChatHistoryDbContext> options)
    : DbContext(options)
{
    public DbSet<ChatMessageEntity> Messages => Set<ChatMessageEntity>();
    public DbSet<ChatMemoryFileEntity> MemoryFiles => Set<ChatMemoryFileEntity>();

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

        modelBuilder.Entity<ChatMemoryFileEntity>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.ConversationId,
                x.NamespaceKey,
                x.Path
            }).IsUnique();

            entity.Property(x => x.ConversationId)
                .IsRequired();

            entity.Property(x => x.NamespaceKey)
                .IsRequired();

            entity.Property(x => x.Path)
                .IsRequired();

            entity.Property(x => x.Content)
                .IsRequired();
        });
    }
}