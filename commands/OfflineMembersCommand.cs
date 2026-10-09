namespace MintChatManager.Commands;

public sealed class OfflineMembersCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/offlinelist";
    public string Description => "Показать список офлайн-участников беседы";

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var offlineContext = context with { CommandName = Name };
        return new OnlineMembersCommand(store, administratorIds)
            .ExecuteAsync(api, offlineContext, arguments, cancellationToken);
    }
}
