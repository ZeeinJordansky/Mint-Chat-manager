namespace MintChatManager.Commands;

public sealed class EchoCommand : IBotCommand
{
    public string Name => "/echo";
    public string Description => "Повторить текст после команды";

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var message = string.IsNullOrWhiteSpace(arguments)
            ? "Использование: /echo текст"
            : arguments;
        return api.SendMessageAsync(context.PeerId, message, cancellationToken);
    }
}
