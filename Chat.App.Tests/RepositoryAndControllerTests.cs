using Chat.App.API.Controllers;
using Chat.App.API.Database;
using Chat.App.API.Database.Entities;
using Chat.App.API.Models;
using Chat.App.API.Services;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using ConversationEntity = Chat.App.API.Database.Entities.Conversation;
using ConversationListModel = Chat.App.API.Models.Conversation;

namespace Chat.App.Tests;

public class RepositoryAndControllerTests
{
    [Fact]
    public async Task ConversationRepository_CreatesReadsListsRenamesAndDeletes()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();

            var repo = new ConversationRepository(db);
            var created = await repo.CreateAsync("First title");
            Assert.Equal("First title", created.Title);

            var fetched = await repo.GetAsync(created.Id);
            Assert.NotNull(fetched);
            Assert.Equal(created.Id, fetched!.Id);

            var list = await repo.ListAsync();
            Assert.Single(list);

            var renamed = await repo.RenameAsync(created.Id, " Updated title ");
            Assert.NotNull(renamed);
            Assert.Equal("Updated title", renamed!.Title);

            var deleted = await repo.DeleteAsync(created.Id);
            Assert.True(deleted);
            Assert.Null(await repo.GetAsync(created.Id));
        }

        await connection.CloseAsync();
    }

    [Fact]
    public void DatabaseDependencies_AreRegisteredAndConfigured()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment { EnvironmentName = Environments.Development });

        services.AddDatabaseServices("Data Source=coverage.db");

        using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<IConversationRepository>();
        Assert.NotNull(repository);
    }

    [Fact]
    public async Task ConversationsController_HandlesListRenameDeleteAndValidation()
    {
        var repo = new StubConversationRepository();
        var logger = new LoggerFactory().CreateLogger<ConversationsController>();
        var controller = new ConversationsController(repo, logger);

        var listResult = await controller.List();
        var ok = Assert.IsType<OkObjectResult>(listResult.Result);
        var list = Assert.IsType<List<ConversationListModel>>(ok.Value);
        Assert.Equal(2, list.Count);

        var invalid = await controller.Rename(Guid.NewGuid(), new RenameConversationRequest("  "), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(invalid.Result);

        var update = await controller.Rename(repo.ConversationId, new RenameConversationRequest("Renamed"), CancellationToken.None);
        var okRename = Assert.IsType<OkObjectResult>(update.Result);
        var renamed = Assert.IsType<ConversationListModel>(okRename.Value);
        Assert.Equal("Renamed", renamed.Title);

        var deleteResult = await controller.Delete(repo.ConversationId, CancellationToken.None);
        var deleteOk = Assert.IsType<OkObjectResult>(deleteResult);
        Assert.True((bool)((dynamic)deleteOk.Value!).deleted);

        var missingDelete = await controller.Delete(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(missingDelete);
    }

    [Fact]
    public async Task ChatController_ValidatesMessagesAndStreamsResponses()
    {
        var repo = new StubConversationRepository();
        var logger = new LoggerFactory().CreateLogger<ChatController>();
        var agent = new StubAgentService();
        var controller = new ChatController(agent, repo, logger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var blank = await controller.Chat(new ChatRequest(Guid.NewGuid(), "   "), CancellationToken.None);
        Assert.IsAssignableFrom<IResult>(blank);

        var conversationId = repo.ConversationId;
        var result = await controller.Chat(new ChatRequest(conversationId, "hello there"), CancellationToken.None);
        Assert.IsAssignableFrom<IResult>(result);

        var services = new ServiceCollection().AddLogging();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
        await result.ExecuteAsync(httpContext);
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var output = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
        Assert.Contains("done", output, StringComparison.OrdinalIgnoreCase);

        var get = await controller.Get(conversationId, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(get.Result);
        var detail = Assert.IsType<ConversationDetail>(ok.Value);
        Assert.Equal(conversationId, detail.Id);
        Assert.NotEmpty(detail.Messages);
    }

    [Fact]
    public void Models_AndEntityTypes_ExposeExpectedValues()
    {
        var conversation = new ConversationEntity
        {
            Id = Guid.NewGuid(),
            Title = "Title",
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };

        Assert.Equal("Title", conversation.Title);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), conversation.CreatedAt);

        var request = new ChatRequest(conversation.Id, "Hi");
        Assert.Equal("Hi", request.Message);

        var memoryFile = new ConversationMemoryFile("/note.md", "text", 4, DateTime.UtcNow);
        Assert.Equal("/note.md", memoryFile.Path);

        var detail = new ConversationDetail(conversation.Id, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt, new List<Message> { new("user", "msg", null) }, new List<ConversationMemoryFile> { memoryFile });
        Assert.Equal(1, detail.MessageCount);
        Assert.Equal(1, detail.FileCount);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Chat.App.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StubAgentService : IAgentService
    {
        public async IAsyncEnumerable<AgentResponseUpdate> StreamAsync(string conversationId, ChatMessage message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(string conversationId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChatMessage>>([
                new ChatMessage(ChatRole.User, "hello there"),
                new ChatMessage(ChatRole.Assistant, "Hi there")
            ]);

        public Task<IReadOnlyList<ConversationMemoryFile>> GetMemoryFilesAsync(string conversationId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConversationMemoryFile>>([
                new ConversationMemoryFile("/notes.txt", "body", 4, DateTime.UtcNow)
            ]);
    }

    private sealed class StubConversationRepository : IConversationRepository
    {
        public Guid ConversationId { get; } = Guid.NewGuid();

        public Task<ConversationEntity> CreateAsync(string? title = null, CancellationToken ct = default)
            => Task.FromResult(new ConversationEntity { Id = ConversationId, Title = title ?? "New Conversation", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        public Task<ConversationEntity?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<ConversationEntity?>(id == ConversationId ? new ConversationEntity { Id = id, Title = "Existing", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow } : null);

        public Task<List<ConversationEntity>> ListAsync(int skip = 0, int limit = 100, CancellationToken ct = default)
            => Task.FromResult(new List<ConversationEntity>
            {
                new() { Id = Guid.NewGuid(), Title = "A", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { Id = Guid.NewGuid(), Title = "B", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            });

        public Task<ConversationEntity?> RenameAsync(Guid id, string title, CancellationToken ct = default)
        {
            if (id != ConversationId)
            {
                return Task.FromResult<ConversationEntity?>(null);
            }

            return Task.FromResult<ConversationEntity?>(new ConversationEntity { Id = id, Title = title.Trim(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(id == ConversationId);
    }
}
