using Xunit;

namespace Mem0Sharp.Tests;

public sealed class OpenAiIntegrationTests
{
    private const string RunLiveTestsVariable = "MEM0SHARP_RUN_OPENAI_TESTS";

    [Fact]
    public void ExampleConfigurationCanCreateChatAndEmbeddingClient()
    {
        var configuration = OpenAiTestConfiguration.Load(ConfigurationPath("testsettings.example.yaml"));
        using var httpClient = new HttpClient();

        var client = configuration.OpenAi.CreateClient(httpClient);

        Assert.IsAssignableFrom<IChatCompletionClient>(client);
        Assert.IsAssignableFrom<IEmbeddingGenerator>(client);
        Assert.Equal(new Uri("https://api.openai.com/"), httpClient.BaseAddress);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LocalConfigurationCallsOpenAiChatAndEmbeddings()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunLiveTestsVariable), "1", StringComparison.Ordinal))
            return;

        var configuration = OpenAiTestConfiguration.Load(ConfigurationPath("testsettings.local.yaml"));
        using var httpClient = new HttpClient();
        var client = configuration.OpenAi.CreateClient(httpClient);

        var completion = await client.CompleteAsync([new Message("user", "Reply with exactly: mem0sharp")]);
        var embedding = await client.GenerateAsync("mem0sharp integration test");

        Assert.Contains("mem0sharp", completion, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(embedding);
    }

    private static string ConfigurationPath(string fileName) => Path.Combine(AppContext.BaseDirectory, fileName);
}