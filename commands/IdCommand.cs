namespace MintChatManager.Commands;

public sealed class IdCommand : IBotCommand
{
    public string Name => "/id";
    public string Description => "Показать ID беседы или пользователя";
    public IReadOnlyCollection<string> Aliases => new[] { "/ид", "/идентификатор" };

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var message =
            "┌─ 🆔 Идентификатор\n" +
            $"│ Peer ID: {context.PeerId}\n" +
            $"│ User ID: {context.UserId}\n" +
            "└────────────────────";

        return context.SendMessageAsync(api, message, cancellationToken);
    }
}
