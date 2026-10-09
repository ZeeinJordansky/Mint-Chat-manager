namespace MintChatManager.Commands;

public sealed class IdCommand : IBotCommand
{
    public string Name => "/id";
    public string Description => "Показать ID беседы или пользователя";

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        return api.SendMessageAsync(
            context.PeerId,
            $"ID беседы или пользователя: {context.PeerId}",
            cancellationToken);
    }
}
