namespace MintChatManager.Commands;

public sealed class TimeCommand : IBotCommand
{
    public string Name => "/время";
    public string Description => "Показать текущее время сервера";

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var message = $"Время сервера: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}";
        return api.SendMessageAsync(context.PeerId, message, cancellationToken);
    }
}
