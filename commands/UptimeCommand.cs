namespace MintChatManager.Commands;

public sealed class UptimeCommand(BotStatistics statistics) : IBotCommand
{
    public string Name => "/аптайм";
    public string Description => "Показать время работы бота";

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var uptime = statistics.Uptime;
        var message =
            $"Бот работает: {uptime.Days} д. {uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
        return api.SendMessageAsync(context.PeerId, message, cancellationToken);
    }
}
