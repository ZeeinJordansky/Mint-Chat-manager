namespace MintChatManager.Commands;

public sealed class UnmuteCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/unmute";
    public string Description => "Снять блокировку чата с участника";
    public IReadOnlyCollection<string> Aliases => new[]
    {
        "/анмут", "/унмут", "/размут", "/снятьмут", "/разглушить", "/размутить", "/измута", "/unm"
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
                api, store, context, administratorIds, requiredRole: 1, cancellationToken))
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        var (targetId, _) = CommandTargetParser.Resolve(context, arguments);
        if (targetId is null)
        {
            await context.SendMessageAsync(
                api,
                $"С помощью этой команды вы сможете снять блокировку чата пользователю.\n\n👉🏻 Для того чтобы использовать эту команду, вам нужно заполнить аргументы в таком формате: {Name} [ссылка/упоминание]",
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

        await api.ChangeConversationMemberRestrictionsAsync(context.PeerId, targetId.Value, 0, "rw", cancellationToken);
        await store.ClearModerationActionAsync(context.PeerId, targetId.Value, "mute", cancellationToken);
        await context.SendMessageAsync(
            api,
            $"🔊 {ModerationCommandAccess.FormatMention(targetId.Value, "Пользователю")} снята блокировка чата " +
            $"модератором {ModerationCommandAccess.FormatMention(context.UserId, "модератором")}.",
            cancellationToken);
    }
}
