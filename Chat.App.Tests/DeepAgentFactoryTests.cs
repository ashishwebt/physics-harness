using DeepHarness;
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
}
