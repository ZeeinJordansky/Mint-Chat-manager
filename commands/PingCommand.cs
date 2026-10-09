namespace MintChatManager.Commands;

public sealed class PingCommand : IBotCommand
{
    public string Name => "/ping";
    public string Description => "Проверить отклик бота";
    public IReadOnlyCollection<string> Aliases => new[] { "/пинг", "/пингг", "/pong", "/понг" };

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        return context.SendMessageAsync(api, "🏓 Понг!", cancellationToken);
    }
}