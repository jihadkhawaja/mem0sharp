using Mem0Sharp;
using Xunit;

namespace Mem0Sharp.Tests;

public sealed class MemoryBehaviorTests
{
    [Fact]
    public void NormalIsTheDefaultBehavior()
    {
        Assert.Equal(MemoryBehavior.Normal, new MemoryAddOptions().Behavior);
    }

    [Fact]
    public async Task NormalBehaviorPreservesTheExistingExtractionPrompt()
    {
        var client = new RecordingChatClient("[]");
        var extractor = new LlmMemoryExtractor(client);

        await extractor.ExtractAsync([new Message("user", "I like tea")]);

        Assert.Equal(
            "Extract durable user facts from the conversation. Return only a JSON array of strings. Ignore greetings, questions, and temporary requests.",
            client.Messages[0].Content);
    }

    [Theory]
    [InlineData(MemoryBehavior.Dreaming, "dream-like memory consolidation")]
    [InlineData(MemoryBehavior.RandomThoughts, "spontaneous thoughts")]
    [InlineData(MemoryBehavior.PersonalMemory, "first-person perspective")]
    public async Task LlmExtractorAppliesTheSelectedBehavior(MemoryBehavior behavior, string expectedInstruction)
    {
        var client = new RecordingChatClient("[\"A shaped memory\"]");
        var extractor = new LlmMemoryExtractor(client);

        var memories = await extractor.ExtractAsync(
            [new Message("user", "I like tea")],
            new MemoryAddOptions { Behavior = behavior, Prompt = "You are a curious, optimistic research assistant." });

        Assert.Equal("A shaped memory", Assert.Single(memories).Text);
        Assert.Contains(expectedInstruction, client.Messages[0].Content);
        Assert.Contains("curious, optimistic research assistant", client.Messages[0].Content);
    }

    [Fact]
    public async Task NonNormalBehaviorShapesDirectTextThroughTheSameService()
    {
        var client = new RecordingChatClient("[\"I noticed that Alice values quiet mornings.\"]");
        var service = new MemoryService(extractor: new LlmMemoryExtractor(client));

        var result = await service.AddAsync("Alice enjoys coffee before sunrise.", new MemoryAddOptions
        {
            UserId = "alice",
            Behavior = MemoryBehavior.PersonalMemory,
            Prompt = "Speak as a thoughtful travel companion."
        });

        Assert.Equal("I noticed that Alice values quiet mornings.", Assert.Single(result.Memories).Text);
        Assert.Equal("user", client.Messages[1].Role);
    }

    [Fact]
    public async Task InferFalseKeepsDirectTextVerbatim()
    {
        var service = new MemoryService();

        var result = await service.AddAsync("Keep this exact text.", new MemoryAddOptions
        {
            Infer = false,
            Behavior = MemoryBehavior.Dreaming
        });

        Assert.Equal("Keep this exact text.", Assert.Single(result.Memories).Text);
    }

    [Fact]
    public async Task NonNormalBehaviorRequiresABehaviorAwareExtractor()
    {
        var service = new MemoryService(extractor: new BasicMemoryExtractor());

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => service.AddAsync(
            [new Message("user", "Think about this")],
            new MemoryAddOptions { Behavior = MemoryBehavior.RandomThoughts }));

        Assert.Contains(nameof(IBehaviorAwareMemoryExtractor), error.Message);
    }

    [Fact]
    public async Task ConflictResolverReceivesBehaviorInstructions()
    {
        var client = new RecordingChatClient("{\"memory\":[]}");
        var resolver = new LlmMemoryConflictResolver(client);

        await resolver.ResolveAsync(
            [new Message("user", "A half-remembered melody")],
            [],
            new MemoryAddOptions { Behavior = MemoryBehavior.Dreaming });

        Assert.Contains("dream-like memory consolidation", client.Messages[0].Content);
    }

    private sealed class RecordingChatClient(string response) : IChatCompletionClient
    {
        public IReadOnlyList<Message> Messages { get; private set; } = [];

        public Task<string> CompleteAsync(IReadOnlyList<Message> messages, CancellationToken cancellationToken = default)
        {
            Messages = messages;
            return Task.FromResult(response);
        }
    }
}