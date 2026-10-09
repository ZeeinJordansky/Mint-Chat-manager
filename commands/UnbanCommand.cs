namespace MintChatManager.Commands;

public sealed class UnbanCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/unban";
    public string Description => "Снять блокировку участника беседы";
    public IReadOnlyCollection<string> Aliases => new[]
    {
        "/разбан", "/анбан", "/унбан", "/unb", "/разбанить", "/снятьбан", "/избана",
        "/sunban", "/сунбан", "/тихийразбан"
    };

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

        if (!await ModerationCommandAccess.CanModerateAsync(
                api, store, context, administratorIds, requiredRole: 2, cancellationToken))
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        var (targetId, _) = CommandTargetParser.Resolve(context, arguments);
        if (targetId is null)
        {
            await context.SendMessageAsync(
                api,
                $"С помощью этой команды вы сможете разблокировать пользователя в беседе.\n\n👉🏻 Для того чтобы использовать эту команду, вам нужно заполнить аргументы в таком формате: {Name} [ссылка/упоминание]",
                cancellationToken);
            return;
        }

        if (!await ModerationCommandAccess.CanTargetAsync(
                api, store, context, administratorIds, targetId.Value, cancellationToken))
        {
            await context.SendMessageAsync(
                api,
                "Вы не можете применить это действие к данному пользователю, так как его должность выше или равна вашей.",
                cancellationToken);
            return;
        }

        await store.ClearModerationActionAsync(context.PeerId, targetId.Value, "ban", cancellationToken);
        if (context.CommandName is "/sunban" or "/сунбан" or "/тихийразбан")
        {
            return;
        }

        await context.SendMessageAsync(
            api,
            $"🔓 {ModerationCommandAccess.FormatMention(targetId.Value, "Пользователю")} снята блокировка " +
            $"модератором {ModerationCommandAccess.FormatMention(context.UserId, "модератором")}.",
            cancellationToken);
    }
}
