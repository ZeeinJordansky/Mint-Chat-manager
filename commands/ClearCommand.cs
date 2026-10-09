namespace MintChatManager.Commands;

public sealed class ClearCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/clear";
    public string Description => "Удалить последние сообщения";
    public decimal MinimumRole => 1;
    public IReadOnlyCollection<string> Aliases => new[]
    {
        "/клир", "/очистить", "/очистка", "/клиар", "/delmsg",
        "/mclear", "/мклир", "/мочистить", "/delmsgs"
    };

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var defaultCount = context.CommandName is "/mclear" or "/мклир" or "/мочистить" or "/delmsgs"
            ? 10
            : 1;
        return ClearMessages.ExecuteAsync(
            api, store, administratorIds, context, arguments, silent: false, defaultCount, cancellationToken);
    }
}
