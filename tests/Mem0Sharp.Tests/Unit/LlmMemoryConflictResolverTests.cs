using Mem0Sharp;
using Xunit;

namespace Mem0Sharp.Tests;

public sealed class LlmMemoryConflictResolverTests
{
    [Fact]
    public async Task ResolveAcceptsCommentsTrailingCommasAndSurroundingText()
    {
        const string response = """
            Here is the result:
            ```json
            {
              // Reconcile the changed city.
              "memory": [
                { "text": "new city", "event": "UPDATE", "id": 0, },
              ]
            }
            ```
            """;
        var existing = new Memory { Id = "memory-id", Text = "old city", UserId = "alice" };
        var resolver = new LlmMemoryConflictResolver(new StubChatClient(response));

        var decision = Assert.Single(await resolver.ResolveAsync(
            [new Message("user", "I moved")], [existing], new MemoryAddOptions()));

        Assert.Equal(MemoryAction.Update, decision.Event);
        Assert.Equal("memory-id", decision.MemoryId);
        Assert.Equal("new city", decision.Text);
    }

    [Fact]
    public async Task ResolveReturnsNoDecisionsForMalformedResponse()
    {
        var resolver = new LlmMemoryConflictResolver(new StubChatClient("not valid { json //"));

        var decisions = await resolver.ResolveAsync([], [], new MemoryAddOptions());

        Assert.Empty(decisions);
    }

    [Fact]
    public async Task ResolveSkipsDecisionFieldsWithUnexpectedTypes()
    {
        const string response = """
            { "memory": [42, { "text": 7, "event": true }, { "text": "valid fact", "event": "ADD" }] }
            """;
        var resolver = new LlmMemoryConflictResolver(new StubChatClient(response));

        var decision = Assert.Single(await resolver.ResolveAsync([], [], new MemoryAddOptions()));

        Assert.Equal(MemoryAction.Add, decision.Event);
        Assert.Equal("valid fact", decision.Text);
    }

    private sealed class StubChatClient(string response) : IChatCompletionClient
    {
        public Task<string> CompleteAsync(
            IReadOnlyList<Message> messages,
            CancellationToken cancellationToken = default) => Task.FromResult(response);
    }
}