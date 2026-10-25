namespace SyntInfo.Application.Options;

public class TelegramOptions : IConfigurableOptions
{
    public const string SectionName = "Telegram";
    string IConfigurableOptions.SectionName => SectionName;

    public string BotToken { get; set; } = string.Empty;
    public string AdminChatId { get; set; } = string.Empty;
    public string SecretToken { get; set; } = string.Empty;
    public bool UseMock { get; set; }
}
