namespace MintChatManager.Commands;

public sealed class WarnCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/warn";
    public string Description => "Выдать предупреждение участнику беседы";
    public IReadOnlyCollection<string> Aliases => new[] { "/варн", "/пред", "/предупреждение" };

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

        var (targetId, reason) = CommandTargetParser.Resolve(context, arguments);
        if (targetId is null)
        {
            await context.SendMessageAsync(
                api,
                $"👉🏻 Для того что бы использовать эту команду вам нужно заполнить аргументы в таком формате: {Name} [ссылка/упоминание] [причина]",
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

        reason = string.IsNullOrWhiteSpace(reason) ? "без причины" : reason;
        var warns = await store.AddModerationActionAsync(
            context.PeerId,
            context.UserId,
            targetId.Value,
            "warn",
            reason,
            TimeSpan.FromDays(7),
            cancellationToken);

        var message =
            $"⚠️ {ModerationCommandAccess.FormatMention(targetId.Value, "Пользователю")} выдано предупреждение " +
            $"({warns}/3) модератором {ModerationCommandAccess.FormatMention(context.UserId, "модератором")} по причине: {reason}\n\n" +
            $"| Хэштеги для быстрого поиска: #WARN #id{context.UserId} #id{targetId.Value}";
        if (warns >= 3)
        {
            await api.RemoveChatUserAsync(context.PeerId, targetId.Value, cancellationToken);
            message += "\nДостигнуто 3/3 предупреждений. Пользователь будет исключён.";
        }

        var keyboard = GameText.CreateCallbackKeyboard(
            "Снять предупреждение",
            $"/unwarn {targetId.Value}",
            "secondary");
        await api.SendMessageWithKeyboardAsync(context.PeerId, message, keyboard, cancellationToken);
    }
}
