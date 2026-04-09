namespace SyntInfo.Application.CQRS.Commands;

public record CleanupOldArticlesCommand(int DaysToKeep = 7);
