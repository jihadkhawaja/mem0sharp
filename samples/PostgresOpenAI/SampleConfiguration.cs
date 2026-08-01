using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

internal sealed class SampleConfiguration
{
    public OpenAiSettings OpenAi { get; init; } = new();
    public PostgresSettings Postgres { get; init; } = new();

    public static SampleConfiguration Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Copy sampleconfig.example.yaml to sampleconfig.local.yaml and add your API key.",
                path);
        }

        using var reader = File.OpenText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        var configuration = deserializer.Deserialize<SampleConfiguration>(reader)
            ?? throw new InvalidDataException($"Configuration '{path}' is empty.");

        if (string.IsNullOrWhiteSpace(configuration.OpenAi.ApiKey))
        {
            throw new InvalidDataException("openAi.apiKey must be configured in sampleconfig.local.yaml.");
        }

        return configuration;
    }
}

internal sealed class OpenAiSettings
{
    public string Endpoint { get; init; } = "https://api.openai.com/";
    public string ApiKey { get; init; } = string.Empty;
    public string ChatModel { get; init; } = "gpt-5-mini";
    public string EmbeddingModel { get; init; } = "text-embedding-3-small";
}

internal sealed class PostgresSettings
{
    public string ConnectionString { get; init; } =
        "Host=localhost;Port=5432;Database=mem0;Username=postgres;Password=postgres";
    public int EmbeddingDimensions { get; init; } = 1536;
    public string TableName { get; init; } = "sample_memories";
}
