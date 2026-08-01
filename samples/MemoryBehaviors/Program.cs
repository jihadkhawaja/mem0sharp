using Mem0Sharp;

var configuration = SampleConfiguration.Load(
    Path.Combine(AppContext.BaseDirectory, "sampleconfig.local.yaml"));

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(configuration.OpenAi.Endpoint, UriKind.Absolute)
};
var openAi = new OpenAiCompatibleClient(
    httpClient,
    configuration.OpenAi.ApiKey,
    configuration.OpenAi.ChatModel,
    configuration.OpenAi.EmbeddingModel);

var memory = new MemoryService(
    embeddings: openAi,
    extractor: new LlmMemoryExtractor(openAi));

Message[] conversation =
[
    new("user", "The smell of rain reminds me of studying astronomy with my grandfather. I still look for Orion when I need courage."),
    new("assistant", "That sounds like both a comforting memory and a personal ritual.")
];

MemoryBehavior[] behaviors =
[
    MemoryBehavior.Normal,
    MemoryBehavior.Dreaming,
    MemoryBehavior.RandomThoughts,
    MemoryBehavior.PersonalMemory
];

foreach (var behavior in behaviors)
{
    var result = await memory.AddAsync(conversation, new MemoryAddOptions
    {
        UserId = "alice",
        AgentId = "mira",
        RunId = $"behavior-{behavior}",
        Behavior = behavior,
        Prompt = behavior == MemoryBehavior.PersonalMemory
            ? "You are Mira, a thoughtful companion who notices emotional meaning and speaks with gentle curiosity."
            : null
    });

    Console.WriteLine($"{behavior}:");
    foreach (var item in result.Memories)
    {
        Console.WriteLine($"  - {item.Text}");
    }
    Console.WriteLine();
}