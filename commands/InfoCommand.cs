namespace MintChatManager.Commands;

public sealed class InfoCommand(PostgresStore store) : IBotCommand
{
    public string Name => "/info";
    public string Description => "Показать информацию о беседе";
    public IReadOnlyCollection<string> Aliases => new[] { "/инфо" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var settings = await store.GetChatSettingsAsync(context.PeerId, cancellationToken);
        var message = string.IsNullOrWhiteSpace(settings.Info)
            ? "Текст информации беседы не установлен. Владелец может установить его командой /setinfo."
            : settings.Info;
        await context.SendMessageAsync(api, message, cancellationToken);
    }
}
