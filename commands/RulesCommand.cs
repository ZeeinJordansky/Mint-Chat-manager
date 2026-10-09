namespace MintChatManager.Commands;

public sealed class RulesCommand(PostgresStore store) : IBotCommand
{
    public string Name => "/rules";
    public string Description => "Показать правила беседы";
    public IReadOnlyCollection<string> Aliases => new[] { "/правила" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var settings = await store.GetChatSettingsAsync(context.PeerId, cancellationToken);
        var message = string.IsNullOrWhiteSpace(settings.Rules)
            ? "Правила беседы не установлены. Владелец может добавить их командой /setrules."
            : settings.Rules;
        await context.SendMessageAsync(api, message, cancellationToken);
    }
}
