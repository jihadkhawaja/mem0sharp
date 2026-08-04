using Mem0Sharp;
using Mem0Sharp.McpSample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var sampleDirectory = Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.Parent!.FullName;
var dataDirectory = Path.Combine(sampleDirectory, "data");
Directory.CreateDirectory(dataDirectory);

await using var store = new SqliteMemoryStore(Path.Combine(dataDirectory, "mem0sharp.db"));
await store.InitializeAsync();

var memory = new MemoryService(store);
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddSingleton<IMemoryService>(memory);
builder.Services
	.AddMcpServer()
	.WithStdioServerTransport()
	.WithTools<McpTools>();

await builder.Build().RunAsync();
