namespace MintChatManager.Commands;

public sealed class RemoveNicknameCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/rnick";
    public string Description => "Удалить Nick_Name участника беседы";
    public decimal MinimumRole => 1;
    public IReadOnlyCollection<string> Aliases => new[] { "/рник", "/делник", "/удалитьник" };

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
                api, store, context, administratorIds, requiredRole: MinimumRole, cancellationToken))
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        var (targetId, _) = CommandTargetParser.Resolve(context, arguments);
        if (targetId is null || targetId <= 0)
        {
            await context.SendMessageAsync(
                api,
                $"Укажите пользователя: {Name} [ссылка/упоминание] или ответьте на его сообщение.",
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

        if (!await store.RemoveChatNicknameAsync(context.PeerId, targetId.Value, cancellationToken))
        {
            await context.SendMessageAsync(api, "У пользователя отсутствует Nick_Name.", cancellationToken);
            return;
        }

        await context.SendMessageAsync(
            api,
            $"✏️ У {ModerationCommandAccess.FormatMention(targetId.Value, "пользователя")} снят Nick_Name " +
            $"{ModerationCommandAccess.FormatMention(context.UserId, "модератором")}.",
            cancellationToken,
            quoteCommand: false);
    }
}
