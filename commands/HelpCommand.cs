namespace MintChatManager.Commands;

public sealed class HelpCommand(IReadOnlyCollection<IBotCommand> commands) : IBotCommand
{
    public string Name => "/help";
    public string Description => "Показать список команд";
    public IReadOnlyCollection<string> Aliases => new[] { "/помощь", "/хелп", "/команды" };

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var lines = commands
            .Where(command => command is not IInternalBotCommand)
            .Select(command => $"{GetCommandList(command)} — {command.Description}")
            .ToArray();

        var message = "┌─ 📚 Список команд\n" +
                      string.Join('\n', lines.Select(line => $"│ {line}")) +
                      "\n└────────────────────";
        return context.SendMessageAsync(api, message, cancellationToken);
    }

    private static string GetCommandList(IBotCommand command)
    {
        var names = new List<string> { command.Name };
        if (command.Aliases is not null)
        {
            names.AddRange(command.Aliases.Where(name => !string.IsNullOrWhiteSpace(name)));
        }

        return string.Join(", ", names.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
