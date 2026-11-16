using System.Runtime.CompilerServices;
using DeepHarness;
using DeepHarness.Agents;
using DeepHarness.Backend;
using DeepHarness.HistoryProvider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chat.App.Tests;

public class DeepAgentFactoryTests
{
    [Fact]
    public void BuildSystemPrompt_IncludesGenericToolGuidanceAndOptionalSpecificPrompt()
    {
        var prompt = DeepAgentFactory.BuildSystemPrompt("Focus on code review tasks.");

        Assert.Contains("tool", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Focus on code review tasks.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSystemPrompt_ListsAvailablePathsForCompositeBackend()
    {
        var defaultRoot = Path.Combine(Path.GetTempPath(), $"deepagent-prompt-default-{Guid.NewGuid():N}");
        Directory.CreateDirectory(defaultRoot);
        var routeRoot = Path.Combine(Path.GetTempPath(), $"deepagent-prompt-route-{Guid.NewGuid():N}");
        Directory.CreateDirectory(routeRoot);

        try
        {
            var backend = new CompositeBackend(
                new FilesystemBackend(defaultRoot),
                new Dictionary<string, IBackend> { ["/artifacts/"] = new FilesystemBackend(routeRoot) });

            var prompt = DeepAgentFactory.BuildSystemPrompt(null, backend);

            Assert.Contains("Available workspace paths", prompt, StringComparison.Ordinal);
            Assert.Contains("/artifacts/", prompt, StringComparison.Ordinal);
            Assert.Contains("default workspace root", prompt, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(defaultRoot, recursive: true);
            Directory.Delete(routeRoot, recursive: true);
        }
    }

    [Fact]
    public void DefaultAgentName_IsUsedWhenNoNameIsProvided()
    {
        Assert.Equal("DeepAgent", DeepAgentFactory.DefaultAgentName);
    }

    [Fact]
    public void AddDeepHarness_RegistersHistoryProviderAndFactories()
    {
        var services = new ServiceCollection();
        var connectionString = $"Data Source={Path.Combine(Path.GetTempPath(), $"deepagent-di-{Guid.NewGuid():N}.db")}";

        services.AddDeepHarness(new DeepHarnessOptions
        {
            ConnectionStringMessages = connectionString,
            AgentName = "DeepAgent",
            EnsureDatabaseCreatedOnStartup = false,
            ConfigureHistoryDb = dbContextOptions =>
            {
                dbContextOptions.UseSqlite(connectionString);
            },
            ConfigureStoreDb = dbContextOptions =>
            {
                dbContextOptions.UseSqlite(connectionString);
            }
        });

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<DbChatHistoryProvider>());
        Assert.NotNull(provider.GetService<IDbContextFactory<ChatHistoryDbContext>>());
        Assert.NotNull(provider.GetService<IDbContextFactory<SqlStoreDbContext>>());
    }

    [Fact]
    public async Task ChatClientAgent_ExecutesToolCallBeforeReturningFinalResponse()
    {
        var fakeClient = new FakeToolChatClient();
        var tool = AIFunctionFactory.Create((string message) => $"Echo: {message}", "echo");
        var agent = new ChatClientAgent(
            fakeClient,
            new ChatClientAgentOptions
            {
                ChatHistoryProvider = new InMemoryChatHistoryProvider(),
                Name = "tool-agent",
                ChatOptions = new ChatOptions
                {
                    Instructions = "Use tools when needed.",
                    ToolMode = ChatToolMode.Auto,
                    Tools = [tool]
                }
            });

        Guid conversationId = Guid.NewGuid();
        AgentSession session = await agent.CreateSessionAsync(conversationId.ToString());
        var response = await agent.RunAsync([new ChatMessage(ChatRole.User, "hello")], session);

        Assert.True(fakeClient.ToolWasCalled);
        Assert.Contains("Echo: hello", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatClientAgent_ContinuesUntilNoMoreToolCallsRemain()
    {
        var fakeClient = new FakeToolChatClientMultiRound();
        var tool = AIFunctionFactory.Create((string message) => $"Echo: {message}", "echo");
        var agent = new ChatClientAgent(
            fakeClient,
            new ChatClientAgentOptions
            {
                ChatHistoryProvider = new InMemoryChatHistoryProvider(),
                Name = "tool-agent",
                ChatOptions = new ChatOptions
                {
                    Instructions = "Use tools when needed.",
                    ToolMode = ChatToolMode.Auto,
                    Tools = [tool]
                }
            });
        Guid conversationId = Guid.NewGuid();
        AgentSession session = await agent.CreateSessionAsync(conversationId.ToString());

        var response = await agent.RunAsync([new ChatMessage(ChatRole.User, "hello")], session);

        Assert.Equal("Final after second tool round", response.Text);
        Assert.Equal(2, fakeClient.ToolCallCount);
    }

    [Fact]
    public async Task ChatClientAgent_StreamingLoop_ContinuesUntilNoMoreToolCallsRemain()
    {
        var fakeClient = new FakeStreamingToolChatClientMultiRound();
        var tool = AIFunctionFactory.Create((string message) => $"Echo: {message}", "echo");
        var agent = new ChatClientAgent(
            fakeClient,
            new ChatClientAgentOptions
            {
                ChatHistoryProvider = new InMemoryChatHistoryProvider(),
                Name = "tool-agent",
                ChatOptions = new ChatOptions
                {
                    Instructions = "Use tools when needed.",
                    ToolMode = ChatToolMode.Auto,
                    Tools = [tool]
                }
            });

        var updates = new List<string>();
        Guid conversationId = Guid.NewGuid();
        AgentSession session = await agent.CreateSessionAsync(conversationId.ToString());

        await foreach (var update in agent.RunStreamingAsync([new ChatMessage(ChatRole.User, "hello")], session))
        {
            if (!string.IsNullOrWhiteSpace(update.Text))
            {
                updates.Add(update.Text);
            }
        }

        Assert.Contains("Final after second tool round", updates, StringComparer.Ordinal);
        Assert.Equal(2, fakeClient.ToolCallCount);
    }

    [Fact]
    public async Task ChatClientAgent_PersistsToolMessagesAcrossSecondUserTurn()
    {
        var fakeClient = new FakeSecondTurnToolChatClient();
        var tool = AIFunctionFactory.Create((string message) => $"Echo: {message}", "echo");
        var agent = new ChatClientAgent(
            fakeClient,
            new ChatClientAgentOptions
            {
                ChatHistoryProvider = new InMemoryChatHistoryProvider(),
                Name = "tool-agent",
                ChatOptions = new ChatOptions
                {
                    Instructions = "Use tools when needed.",
                    ToolMode = ChatToolMode.Auto,
                    Tools = [tool]
                }
            });

        var session = await agent.CreateSessionAsync("conversation-with-tools");

        var first = await agent.RunAsync([new ChatMessage(ChatRole.User, "first turn")], session);
        Assert.Contains("Final response after first turn", first.Text, StringComparison.Ordinal);

        var second = await agent.RunAsync([new ChatMessage(ChatRole.User, "second turn")], session);
        Assert.Contains("Second turn final", second.Text, StringComparison.Ordinal);
        Assert.Equal(1, fakeClient.ToolCallCount);
    }

    [Fact]
    public async Task ChatClientAgent_PersistsStructuredToolResultsForModelConsumption()
    {
        var fakeClient = new FakeToolChatClient();
        var tool = AIFunctionFactory.Create((string message) => $"Echo: {message}", "echo");
        var historyProvider = new InMemoryChatHistoryProvider();
        var agent = new ChatClientAgent(
            fakeClient,
            new ChatClientAgentOptions
            {
                ChatHistoryProvider = historyProvider,
                Name = "tool-agent",
                ChatOptions = new ChatOptions
                {
                    Instructions = "Use tools when needed.",
                    ToolMode = ChatToolMode.Auto,
                    Tools = [tool]
                }
            });

        var session = await agent.CreateSessionAsync("structured-tool-result-test");
        await agent.RunAsync([new ChatMessage(ChatRole.User, "hello")], session);

        var context = new InvokingContext(agent, session, []);
        var messages = (await historyProvider.InvokingAsync(context)).ToList();

        Assert.Contains(messages, m => m.Role == ChatRole.Tool && m.Contents.OfType<FunctionResultContent>().Any());
    }

    [Fact]
    public async Task ChatClientAgent_NoOrphanedToolCallsInNonStreamingPersistedHistory()
    {
        var fakeClient = new FakeToolChatClientMultiRound();
        var tool = AIFunctionFactory.Create((string message) => $"Echo: {message}", "echo");
        var historyProvider = new InMemoryChatHistoryProvider();
        var agent = new ChatClientAgent(
            fakeClient,
            new ChatClientAgentOptions
            {
                ChatHistoryProvider = historyProvider,
                Name = "tool-agent",
                ChatOptions = new ChatOptions
                {
                    Instructions = "Use tools when needed.",
                    ToolMode = ChatToolMode.Auto,
                    Tools = [tool]
                }
            });

        var session = await agent.CreateSessionAsync("orphan-test");
        await agent.RunAsync([new ChatMessage(ChatRole.User, "hello")], session);

        var context = new InvokingContext(agent, session, []);
        var messages = (await historyProvider.InvokingAsync(context)).ToList();

        AssertNoOrphanedToolCalls(messages);
    }

    [Fact]
    public async Task ChatClientAgent_NoOrphanedToolCallsInStreamingPersistedHistory()
    {
        var fakeClient = new FakeStreamingToolChatClientMultiRound();
        var tool = AIFunctionFactory.Create((string message) => $"Echo: {message}", "echo");
        var historyProvider = new InMemoryChatHistoryProvider();
        var agent = new ChatClientAgent(
            fakeClient,
            new ChatClientAgentOptions
            {
                ChatHistoryProvider = historyProvider,
                Name = "tool-agent",
                ChatOptions = new ChatOptions
                {
                    Instructions = "Use tools when needed.",
                    ToolMode = ChatToolMode.Auto,
                    Tools = [tool]
                }
            });

        var session = await agent.CreateSessionAsync("orphan-streaming-test");

        await foreach (var _ in agent.RunStreamingAsync([new ChatMessage(ChatRole.User, "hello")], session))
        {
            // Consume the stream
        }

        var context = new InvokingContext(agent, session, []);
        var messages = (await historyProvider.InvokingAsync(context)).ToList();

        AssertNoOrphanedToolCalls(messages);
    }

    private static void AssertNoOrphanedToolCalls(List<ChatMessage> messages)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (msg.Role == ChatRole.Assistant && msg.Contents.OfType<FunctionCallContent>().Any())
            {
                Assert.True(
                    i + 1 < messages.Count && messages[i + 1].Role == ChatRole.Tool,
                    $"Assistant tool-call message at index {i} must be followed by a Tool message.");
            }
        }
    }

    private static string? GetToolResultText(ChatMessage? message)
    {
        if (message is null)
        {
            return null;
        }

        var result = message.Contents.OfType<FunctionResultContent>().LastOrDefault();
        return result?.Result?.ToString();
    }

    private sealed class FakeToolChatClient : IChatClient
    {
        public bool ToolWasCalled { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (messages.Any(m => m.Text?.Contains("hello", StringComparison.OrdinalIgnoreCase) == true) && !ToolWasCalled)
            {
                ToolWasCalled = true;
                var toolCall = new FunctionCallContent("call-1", "echo", new Dictionary<string, object?> { ["message"] = "hello" });
                return Task.FromResult(new ChatResponse
                {
                    Messages = [new ChatMessage(ChatRole.Assistant, [toolCall])]
                });
            }

            return Task.FromResult(new ChatResponse
            {
                Messages = [new ChatMessage(ChatRole.Assistant, "Echo: hello")]
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }

    private sealed class FakeToolChatClientMultiRound : IChatClient
    {
        public int ToolCallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var history = messages.ToList();
            var hasToolResult = history.Any(m => m.Role == ChatRole.Tool);

            if (!hasToolResult)
            {
                ToolCallCount++;
                var toolCall = new FunctionCallContent("call-1", "echo", new Dictionary<string, object?> { ["message"] = "first" });
                return Task.FromResult(new ChatResponse
                {
                    Messages = [new ChatMessage(ChatRole.Assistant, [toolCall])]
                });
            }

            var toolMessage = GetToolResultText(history.LastOrDefault(m => m.Role == ChatRole.Tool));
            if (toolMessage == "Echo: first")
            {
                ToolCallCount++;
                var toolCall = new FunctionCallContent("call-2", "echo", new Dictionary<string, object?> { ["message"] = "second" });
                return Task.FromResult(new ChatResponse
                {
                    Messages = [new ChatMessage(ChatRole.Assistant, [toolCall])]
                });
            }

            return Task.FromResult(new ChatResponse
            {
                Messages = [new ChatMessage(ChatRole.Assistant, "Final after second tool round")]
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }

    private sealed class FakeStreamingToolChatClientMultiRound : IChatClient
    {
        public int ToolCallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var history = messages.ToList();
            var hasToolResult = history.Any(m => m.Role == ChatRole.Tool);

            if (!hasToolResult)
            {
                ToolCallCount++;
                var toolCall = new FunctionCallContent("call-1", "echo", new Dictionary<string, object?> { ["message"] = "first" });
                yield return new ChatResponseUpdate(ChatRole.Assistant, [toolCall]);
                yield break;
            }

            var toolMessage = GetToolResultText(history.LastOrDefault(m => m.Role == ChatRole.Tool));
            if (toolMessage == "Echo: first")
            {
                ToolCallCount++;
                var toolCall = new FunctionCallContent("call-2", "echo", new Dictionary<string, object?> { ["message"] = "second" });
                yield return new ChatResponseUpdate(ChatRole.Assistant, [toolCall]);
                yield break;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, "Final after second tool round");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }

    private sealed class FakeSecondTurnToolChatClient : IChatClient
    {
        public int ToolCallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var history = messages.ToList();
            var lastUserText = history.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
            var hasToolResult = history.Any(m => m.Role == ChatRole.Tool);

            if (lastUserText?.Contains("second turn", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Task.FromResult(new ChatResponse
                {
                    Messages = [new ChatMessage(ChatRole.Assistant, "Second turn final")]
                });
            }

            if (!hasToolResult && lastUserText?.Contains("first turn", StringComparison.OrdinalIgnoreCase) == true)
            {
                ToolCallCount++;
                var call = new FunctionCallContent("call-1", "echo", new Dictionary<string, object?> { ["message"] = "first" });
                return Task.FromResult(new ChatResponse
                {
                    Messages = [new ChatMessage(ChatRole.Assistant, [call])]
                });
            }

            if (hasToolResult && lastUserText?.Contains("first turn", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Task.FromResult(new ChatResponse
                {
                    Messages = [new ChatMessage(ChatRole.Assistant, "Final response after first turn")]
                });
            }

            return Task.FromResult(new ChatResponse
            {
                Messages = [new ChatMessage(ChatRole.Assistant, "Unexpected state")]
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }
}
