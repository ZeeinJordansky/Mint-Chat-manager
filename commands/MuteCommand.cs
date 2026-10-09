namespace MintChatManager.Commands;

public sealed class MuteCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/mute";
    public string Description => "Запретить участнику писать в беседе на срок";
    public decimal MinimumRole => 1;
    public IReadOnlyCollection<string> Aliases => new[]
    {
        "/мут", "/замутить", "/км", "/заглушить", "/мутить", "/датьмут", "/m",
        "/smute", "/смут", "/тихиймут"
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

        var (targetId, remaining) = CommandTargetParser.Resolve(context, arguments);
        if (targetId is null)
        {
            await context.SendMessageAsync(
                api,
                $"С помощью этой команды вы сможете запретить пользователю писать в беседу на определённый срок.\n\n👉🏻 Для того чтобы использовать эту команду, вам нужно заполнить аргументы в таком формате: {Name} [ссылка/упоминание] [срок] [причина]",
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

        var (duration, display, reason) = ModerationDurationParser.ParseMute(remaining);
        await api.ChangeConversationMemberRestrictionsAsync(
            context.PeerId, targetId.Value, checked((int)duration.TotalSeconds), "ro", cancellationToken);
        await store.AddModerationActionAsync(
            context.PeerId, context.UserId, targetId.Value, "mute", reason, duration, cancellationToken);

        if (context.CommandName is "/smute" or "/смут" or "/тихиймут")
        {
            return;
        }

        var message =
            $"🔇 {ModerationCommandAccess.FormatMention(targetId.Value, "Пользователю")} выдана блокировка чата сроком на {display} " +
            $"модератором {ModerationCommandAccess.FormatMention(context.UserId, "модератором")} по причине: {reason}\n\n" +
            $"| Блокировка чата действует до: {DateTimeOffset.UtcNow.Add(duration):dd.MM.yyyy HH:mm} UTC\n" +
            $"| Хэштеги для быстрого поиска: #MUTE #id{context.UserId} #id{targetId.Value}";
        var keyboard = GameText.CreateCallbackKeyboard(
            "Снять блокировку чата",
            $"/unmute {targetId.Value}",
            "secondary");
        await api.SendMessageWithKeyboardAsync(context.PeerId, message, keyboard, cancellationToken);
    }
}
