using Mem0Sharp;

using var httpClient = new HttpClient();
var ollama = new OllamaClient(
    httpClient,
    chatModel: "llama3.2",
    embeddingModel: "nomic-embed-text",
    endpoint: new Uri("http://localhost:11434/"));

var memory = new MemoryService(
    embeddings: ollama,
    extractor: new LlmMemoryExtractor(ollama));

var added = await memory.AddAsync(
[
    new Message("user", "My name is Alice and I prefer dark mode."),
    new Message("assistant", "I will remember that.")
],
userId: "alice");

Console.WriteLine($"Extracted {added.Memories.Count} memories.");

var results = await memory.SearchAsync(
    "What display theme does Alice prefer?",
    new MemoryFilter(UserId: "alice"),
    topK: 3);

foreach (var result in results)
{
    Console.WriteLine($"{result.Score:F3}: {result.Memory.Text}");
}
