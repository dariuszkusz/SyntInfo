namespace SyntInfo.Application.Options;

public class LocalLlmOptions : IConfigurableOptions
{
    public const string SectionName = "Llm";
    string IConfigurableOptions.SectionName => SectionName;

    public string BaseUrl { get; set; } = "http://localhost:12434/";
    public string ModelName { get; set; } = "ai/llama3.2:latest";
    public bool UseMock { get; set; }
}
