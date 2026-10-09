namespace MintChatManager.Commands;

public sealed class HelpCommand(IReadOnlyCollection<IBotCommand> commands) : IBotCommand
{
    public string Name => "/help";
    public string Description => "Показать список команд";

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var message = "Доступные команды:\n" +
                      string.Join('\n', commands.Select(command => $"{command.Name} — {command.Description}"));
        return api.SendMessageAsync(context.PeerId, message, cancellationToken);
    }
}
