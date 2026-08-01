using Mem0Sharp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

var configuration = SampleConfiguration.Load(
    Path.Combine(AppContext.BaseDirectory, "sampleconfig.local.yaml"));

var openAiClient = new OpenAIClient(
    new System.ClientModel.ApiKeyCredential(configuration.OpenAi.ApiKey),
    new OpenAIClientOptions
    {
        Endpoint = new Uri(configuration.OpenAi.Endpoint)
    });
var memory = new MemoryService();

var agent = new ChatClientAgent(
    openAiClient.GetChatClient(configuration.OpenAi.ChatModel).AsIChatClient(),
    new ChatClientAgentOptions
    {
        ChatOptions = new Microsoft.Extensions.AI.ChatOptions
        {
            Instructions = "You are a helpful assistant. Use remembered preferences when they are relevant, and do not invent memories."
        },
        AIContextProviders = [new Mem0ContextProvider(memory, "alice")]
    });

var session = await agent.CreateSessionAsync();
Console.WriteLine("Tell the agent something it should remember, then ask about it later. Type 'exit' to stop.\n");

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();
    if (string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
        break;
    if (string.IsNullOrWhiteSpace(input))
        continue;

    var response = await agent.RunAsync(input, session);
    await memory.AddAsync(input, new MemoryAddOptions
    {
        UserId = "alice",
        Infer = false
    });
    Console.WriteLine($"Agent: {response}\n");
}

internal sealed class Mem0ContextProvider : AIContextProvider
{
    private readonly MemoryService _memory;
    private readonly string _userId;

    public Mem0ContextProvider(MemoryService memory, string userId)
    {
        _memory = memory;
        _userId = userId;
    }

    protected override ValueTask StoreAIContextAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var memories = await _memory.GetAllAsync(
            new MemoryFilter(UserId: _userId),
            cancellationToken: cancellationToken);

        if (memories.Count == 0)
            return new AIContext();

        var remembered = string.Join(
            Environment.NewLine,
            memories.Select(memory => $"- {memory.Text}"));

        return new AIContext
        {
            Instructions = $"Relevant memories for this user:\n{remembered}"
        };
    }
}
