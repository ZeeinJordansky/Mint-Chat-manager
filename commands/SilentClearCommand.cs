namespace MintChatManager.Commands;

public sealed class SilentClearCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/sclear";
    public string Description => "Удалить сообщения без ответа бота";
    public IReadOnlyCollection<string> Aliases =>
        new[] { "/склир", "/тихийклир", "/сочистить", "/smclear", "/смклир", "/смклиар", "/тихийочистить" };

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var defaultCount = context.CommandName is "/smclear" or "/смклир" or "/смклиар" or "/тихийочистить"
            ? 10
            : 1;
        return ClearMessages.ExecuteAsync(
            api, store, administratorIds, context, arguments, silent: true, defaultCount, cancellationToken);
    }
}
