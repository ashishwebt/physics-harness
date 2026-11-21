using DeepHarness.HistoryProvider;
using Microsoft.EntityFrameworkCore;

namespace DeepHarness.Backend;

public sealed class SqlStoreDbContext(DbContextOptions<SqlStoreDbContext> options)
    : DbContext(options)
{
    public DbSet<ChatMemoryFileEntity> MemoryFiles => Set<ChatMemoryFileEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
