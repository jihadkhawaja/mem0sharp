using Mem0Sharp;

var memory = new MemoryService();

var added = await memory.AddAsync(
    "I prefer dark mode and Vim keybindings",
    userId: "alice",
    metadata: new Dictionary<string, string> { ["source"] = "sample" });

var memoryId = added.Memories.Single().Id;
var results = await memory.SearchAsync(
    "Which editor settings does Alice prefer?",
    new MemoryFilter(UserId: "alice"),
    topK: 3);

foreach (var result in results)
{
    Console.WriteLine($"{result.Score:F3}: {result.Memory.Text}");
}

await memory.UpdateAsync(memoryId, "I prefer light mode and Vim keybindings");

Console.WriteLine("\nHistory:");
foreach (var entry in await memory.GetHistoryAsync(memoryId))
{
    Console.WriteLine($"{entry.Event}: {entry.OldMemory ?? "<none>"} -> {entry.NewMemory ?? "<none>"}");
}

await memory.DeleteAsync(memoryId);
Console.WriteLine($"\nRemaining memories: {(await memory.GetAllAsync()).Count}");
