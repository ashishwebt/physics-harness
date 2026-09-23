using Chat.App.API.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chat.App.API.Database;

public interface IConversationRepository
{
    Task<Conversation> CreateAsync(string? title = null, CancellationToken ct = default);
    Task<Conversation?> GetAsync(Guid id, CancellationToken ct = default);
    Task<List<Conversation>> ListAsync(int skip = 0, int limit = 100, CancellationToken ct = default);
    Task<Conversation?> RenameAsync(Guid id, string title, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public class ConversationRepository : IConversationRepository
{
    private readonly AppDbContext _db;

    public ConversationRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Conversation> CreateAsync(string? title = null, CancellationToken ct = default)
    {
        var conversation = new Conversation
        {
            Title = string.IsNullOrWhiteSpace(title) ? "New Conversation" : title.Trim(),
        };

        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync(ct);
        return conversation;
    }

    public async Task<Conversation?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<List<Conversation>> ListAsync(int skip = 0, int limit = 100, CancellationToken ct = default)
    {
        return await _db.Conversations
            .AsNoTracking()
            .OrderByDescending(c => c.UpdatedAt)
            .Skip(skip)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<Conversation?> RenameAsync(Guid id, string title, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
        {
            return null;
        }

        var normalized = (title ?? string.Empty).Trim();
        conversation.Title = string.IsNullOrEmpty(normalized) ? "New Conversation" : normalized;
        conversation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return conversation;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null)
        {
            return false;
        }

        _db.Conversations.Remove(conversation);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
