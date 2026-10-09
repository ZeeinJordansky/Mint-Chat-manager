namespace MintChatManager.Commands;

public sealed class KickCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/kick";
    public string Description => "Исключить участника из беседы";
    public decimal MinimumRole => 1;
    public IReadOnlyCollection<string> Aliases =>
        new[] { "/кик", "/кикнуть", "/исключить", "/выгнать", "/к", "/k" };

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

        var (targetId, reason) = CommandTargetParser.Resolve(context, arguments);
        if (targetId is null)
        {
            await context.SendMessageAsync(
                api,
                $"С помощью этой команды вы сможете исключить пользователя из беседы.\n\n👉🏻 Для того чтобы использовать эту команду, вам нужно заполнить аргументы в таком формате: {Name} [ссылка/упоминание] [причина]",
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

        if (await api.GetConversationOwnerIdAsync(context.PeerId, cancellationToken) == targetId)
        {
            await context.SendMessageAsync(api, "Вы не можете исключить владельца беседы!", cancellationToken);
            return;
        }

        await api.RemoveChatUserAsync(context.PeerId, targetId.Value, cancellationToken);
        reason = string.IsNullOrWhiteSpace(reason) ? "без причины" : reason;
        await store.AddModerationActionAsync(
            context.PeerId, context.UserId, targetId.Value, "kick", reason, null, cancellationToken);
        var message =
            $"🚪 Пользователь {ModerationCommandAccess.FormatMention(targetId.Value, "пользователь")} был(-а) исключён(-а) из этой беседы " +
            $"модератором {ModerationCommandAccess.FormatMention(context.UserId, "модератором")} по причине: {reason}\n\n" +
            $"| Хэштеги для быстрого поиска: #KICK #id{context.UserId} #id{targetId.Value}";
        await context.SendMessageAsync(api, message, cancellationToken);
    }
}
