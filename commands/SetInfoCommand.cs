namespace MintChatManager.Commands;

public sealed class SetInfoCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/setinfo";
    public string Description => "Установить информацию о беседе";
    public decimal MinimumRole => 6;
    public IReadOnlyCollection<string> Aliases => new[] { "/установитьинфо" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (!ModerationCommandAccess.IsConversation(context.PeerId))
        {
            await context.SendMessageAsync(api, "Команда доступна только в беседах!", cancellationToken);
            return;
        }

        var role = await ModerationCommandAccess.GetEffectiveRoleAsync(
            api, store, context.PeerId, context.UserId, administratorIds, cancellationToken);
        if (role < MinimumRole)
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        var info = arguments.Trim();
        if (info.Length == 0)
        {
            await context.SendMessageAsync(api, "Укажите текст информации о беседе!", cancellationToken);
            return;
        }
        if (info.Length > 4_000)
        {
            await context.SendMessageAsync(api, "Текст информации не должен превышать 4 000 символов.", cancellationToken);
            return;
        }

        await store.UpdateChatSettingsAsync(context.PeerId, "info", info, cancellationToken);
        await context.SendMessageAsync(api, "Вы установили новый текст для команды /info.", cancellationToken);
    }
}
