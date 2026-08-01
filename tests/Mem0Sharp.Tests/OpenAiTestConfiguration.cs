using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Mem0Sharp.Tests;

internal sealed class OpenAiTestConfiguration
{
    public OpenAiTestSettings OpenAi { get; init; } = new();

    public static OpenAiTestConfiguration Load(string path)
    {
        using var reader = File.OpenText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        return deserializer.Deserialize<OpenAiTestConfiguration>(reader)
            ?? throw new InvalidDataException($"Test configuration '{path}' is empty.");
    }
}

internal sealed class OpenAiTestSettings
{
    public string Endpoint { get; init; } = "https://api.openai.com/";
    public string ApiKey { get; init; } = string.Empty;
    public string ChatModel { get; init; } = "gpt-4o-mini";
    public string EmbeddingModel { get; init; } = "text-embedding-3-small";

    public OpenAiCompatibleClient CreateClient(HttpClient httpClient)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidDataException("openAi.apiKey must be configured.");

        httpClient.BaseAddress = new Uri(Endpoint, UriKind.Absolute);
        return new OpenAiCompatibleClient(httpClient, ApiKey, ChatModel, EmbeddingModel);
    }
}