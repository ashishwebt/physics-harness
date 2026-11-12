using System.Runtime.CompilerServices;
using DeepHarness;
using DeepHarness.Agents;
using Microsoft.Extensions.AI;
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
    public void DefaultAgentName_IsUsedWhenNoNameIsProvided()
    {
        Assert.Equal("DeepAgent", DeepAgentFactory.DefaultAgentName);
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

            var toolMessage = history.LastOrDefault(m => m.Role == ChatRole.Tool)?.Text;
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

            var toolMessage = history.LastOrDefault(m => m.Role == ChatRole.Tool)?.Text;
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
}
