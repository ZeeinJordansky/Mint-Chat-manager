namespace MintChatManager.Commands;

public sealed class UnwarnCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/unwarn";
    public string Description => "Снять последнее предупреждение участника";
    public IReadOnlyCollection<string> Aliases => new[]
    {
        "/анварн", "/снятьварн", "/снятьпредупреждение", "/снятьпред", "/анпред", "/удалитьварн", "/unw"
    };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (!await ModerationCommandAccess.CanModerateAsync(
                api, store, context, administratorIds, requiredRole: 1, cancellationToken))
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }
        if (!ModerationCommandAccess.IsConversation(context.PeerId))
        {
            await context.SendMessageAsync(api, "Команда доступна только в беседах!", cancellationToken);
            return;
        }

        var (targetId, _) = CommandTargetParser.Resolve(context, arguments);
        if (targetId is null)
        {
            await context.SendMessageAsync(
                api,
                $"👉🏻 Для того что бы использовать эту команду вам нужно заполнить аргументы в таком формате: {Name} [ссылка/упоминание]",
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

        var removed = await store.RemoveLatestWarningAsync(context.PeerId, targetId.Value, cancellationToken);
        var message = removed
            ? $"{ModerationCommandAccess.FormatMention(targetId.Value, "Пользователю")} было снято предупреждение " +
              $"{ModerationCommandAccess.FormatMention(context.UserId, "модератором")}."
            : "У пользователя нет активных предупреждений.";
        await context.SendMessageAsync(api, message, cancellationToken);
    }
}
