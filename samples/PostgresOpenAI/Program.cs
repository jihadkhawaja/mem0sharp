using Mem0Sharp;

var configuration = SampleConfiguration.Load(
    Path.Combine(AppContext.BaseDirectory, "sampleconfig.local.yaml"));

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(configuration.OpenAi.Endpoint, UriKind.Absolute)
};
var provider = new OpenAiCompatibleClient(
    httpClient,
    configuration.OpenAi.ApiKey,
    configuration.OpenAi.ChatModel,
    configuration.OpenAi.EmbeddingModel);

await using var store = new PostgresMemoryStore(new PostgresMemoryStoreOptions
{
    ConnectionString = configuration.Postgres.ConnectionString,
    EmbeddingDimensions = configuration.Postgres.EmbeddingDimensions,
    TableName = configuration.Postgres.TableName
});
await store.InitializeAsync();

var memory = new MemoryService(
    store: store,
    embeddings: provider,
    extractor: new LlmMemoryExtractor(provider));

await memory.AddAsync(
[
    new Message("user", "I live in Lisbon and my favorite language is C#."),
    new Message("assistant", "Thanks, I will remember that.")
],
userId: "alice");

var results = await memory.SearchAsync(
    "Where does Alice live?",
    new MemoryFilter(UserId: "alice"),
    topK: 3);

foreach (var result in results)
{
    Console.WriteLine($"{result.Score:F3}: {result.Memory.Text}");
}
