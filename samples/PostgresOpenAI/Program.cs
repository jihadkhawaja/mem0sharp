using System.ClientModel;
using Mem0Sharp;
using Microsoft.Extensions.AI;
using OpenAI;

var configuration = SampleConfiguration.Load(
    Path.Combine(AppContext.BaseDirectory, "sampleconfig.local.yaml"));

var openAiClient = new OpenAIClient(
    new ApiKeyCredential(configuration.OpenAi.ApiKey),
    new OpenAIClientOptions { Endpoint = new Uri(configuration.OpenAi.Endpoint) });

var chatClient = openAiClient.GetChatClient(configuration.OpenAi.ChatModel).AsIChatClient();
var embeddingGenerator = openAiClient.GetEmbeddingClient(configuration.OpenAi.EmbeddingModel).AsIEmbeddingGenerator();

await using var store = new PostgresMemoryStore(new PostgresMemoryStoreOptions
{
    ConnectionString = configuration.Postgres.ConnectionString,
    EmbeddingDimensions = configuration.Postgres.EmbeddingDimensions,
    TableName = configuration.Postgres.TableName
});
await store.InitializeAsync();

var memory = new MemoryService(
    store: store,
    embeddings: embeddingGenerator,
    extractor: new LlmMemoryExtractor(chatClient));

await memory.AddAsync(
[
    new Message("user", "I live in Lisbon and my favorite language is C#."),
    new Message("assistant", "Thanks, I will remember that.")
],
new MemoryAddOptions { UserId = "alice" });

var results = await memory.SearchAsync(
    "Where does Alice live?",
    new MemorySearchOptions
    {
        Filter = new MemoryFilter(UserId: "alice"),
        TopK = 3
    });

foreach (var result in results)
{
    Console.WriteLine($"{result.Score:F3}: {result.Memory.Text}");
}
