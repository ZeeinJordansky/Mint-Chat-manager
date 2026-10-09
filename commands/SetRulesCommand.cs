namespace MintChatManager.Commands;

public sealed class SetRulesCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/setrules";
    public string Description => "Установить правила беседы";
    public decimal MinimumRole => 6;
    public IReadOnlyCollection<string> Aliases => new[] { "/установитьправила", "/устправила" };

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

        var rules = arguments.Trim();
        if (rules.Length == 0)
        {
            await context.SendMessageAsync(api, "Укажите текст правил!", cancellationToken);
            return;
        }
        if (rules.Length > 4_000)
        {
            await context.SendMessageAsync(api, "Текст правил не должен превышать 4 000 символов.", cancellationToken);
            return;
        }

        await store.UpdateChatSettingsAsync(context.PeerId, "rules", rules, cancellationToken);
        await context.SendMessageAsync(api, "Вы установили новый текст правил беседы для команды /rules.", cancellationToken);
    }
}
