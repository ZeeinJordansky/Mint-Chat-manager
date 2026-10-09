namespace MintChatManager.Commands;

public sealed class SetNicknameCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/snick";
    public string Description => "Установить Nick_Name участнику беседы";
    public decimal MinimumRole => 1;
    public IReadOnlyCollection<string> Aliases => new[] { "/сник", "/сетник", "/ник" };

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

        var (targetId, nickname) = CommandTargetParser.Resolve(context, arguments);
        if (targetId is null)
        {
            await context.SendMessageAsync(
                api,
                $"Укажите участника и никнейм: {Name} [ссылка/упоминание] [никнейм]. Ответьте на сообщение, чтобы указать участника без ссылки.",
                cancellationToken);
            return;
        }
        if (targetId <= 0)
        {
            await context.SendMessageAsync(api, "Nick_Name можно установить только пользователю.", cancellationToken);
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

        nickname = nickname.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (nickname.Length == 0)
        {
            await context.SendMessageAsync(api, "Укажите ник!", cancellationToken);
            return;
        }
        if (nickname.Length > 35)
        {
            await context.SendMessageAsync(api, "Максимальная длина никнейма — 35 символов!", cancellationToken);
            return;
        }

        var previous = await store.GetChatNicknameAsync(context.PeerId, targetId.Value, cancellationToken);
        await store.SetChatNicknameAsync(
            context.PeerId, targetId.Value, nickname, context.UserId, cancellationToken);
        var message = previous is null
            ? $"✏️ [id{targetId.Value}|Пользователю] установлен Nick_Name " +
              $"{ModerationCommandAccess.FormatMention(context.UserId, "модератором")}.\n\n" +
              $"| Nick_Name: {nickname}"
            : $"✏️ [id{targetId.Value}|Пользователю] изменён Nick_Name " +
              $"{ModerationCommandAccess.FormatMention(context.UserId, "модератором")}.\n\n" +
              $"| Старый Nick_Name: {previous.Nickname}\n| Новый Nick_Name: {nickname}";
        await context.SendMessageAsync(api, message, cancellationToken, quoteCommand: false);
    }
}
