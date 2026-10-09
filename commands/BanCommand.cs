namespace MintChatManager.Commands;

public sealed class BanCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/ban";
    public string Description => "Заблокировать участника в беседе";
    public decimal MinimumRole => 2;
    public IReadOnlyCollection<string> Aliases => new[]
    {
        "/бан", "/забанить", "/sban", "/сбан", "/тихийбан"
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

        var (targetId, remaining) = CommandTargetParser.Resolve(context, arguments);
        if (targetId is null)
        {
            await context.SendMessageAsync(
                api,
                $"С помощью этой команды вы сможете заблокировать пользователя в беседе.\n\n👉🏻 Для того чтобы использовать эту команду, вам нужно заполнить аргументы в таком формате: {Name} [ссылка/упоминание] [срок] [причина]",
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
            await context.SendMessageAsync(api, "Вы не можете заблокировать владельца беседы!", cancellationToken);
            return;
        }

        var (duration, reason) = ModerationDurationParser.ParseBan(remaining);
        await api.RemoveChatUserAsync(context.PeerId, targetId.Value, cancellationToken);
        await store.AddModerationActionAsync(
            context.PeerId, context.UserId, targetId.Value, "ban", reason, duration, cancellationToken);

        if (context.CommandName is "/sban" or "/сбан" or "/тихийбан")
        {
            return;
        }

        var keyboard = GameText.CreateCallbackKeyboard(
            "Снять блокировку",
            $"/unban {targetId.Value}",
            "positive",
            "Все блокировки",
            $"/banlist {targetId.Value}",
            "secondary");
        var message =
            $"🔒 {ModerationCommandAccess.FormatMention(targetId.Value, "Пользователю")} выдана блокировка в этой беседе " +
            $"модератором {ModerationCommandAccess.FormatMention(context.UserId, "модератором")} по причине: {reason}\n\n" +
            $"| Хэштеги для быстрого поиска: #BAN #id{context.UserId} #id{targetId.Value}";
        await api.SendMessageWithKeyboardAsync(context.PeerId, message, keyboard, cancellationToken);
    }
}
