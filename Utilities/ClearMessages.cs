namespace MintChatManager;

internal static class ClearMessages
{
    public static async Task ExecuteAsync(
        VkApiClient api,
        PostgresStore store,
        IReadOnlySet<long> administratorIds,
        BotCommandContext context,
        string arguments,
        bool silent,
        int defaultCount,
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

        var tokens = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        var count = defaultCount;
        for (var index = 0; index < tokens.Count; index++)
        {
            if (int.TryParse(tokens[index], out var specifiedCount) && specifiedCount > 0 &&
                !tokens[index].StartsWith("id", StringComparison.OrdinalIgnoreCase))
            {
                count = Math.Clamp(specifiedCount, 1, 100);
                tokens.RemoveAt(index);
                break;
            }
        }

        var targetArguments = string.Join(' ', tokens);
        var (targetId, _) = CommandTargetParser.Resolve(context, targetArguments, allowNumericTarget: false);
        targetId ??= context.ReplyToUserId;
        if (silent && targetId is null && context.ReplyToConversationMessageId is null && tokens.Count == 0)
        {
            await context.SendMessageAsync(
                api,
                $"👉🏻 Для того что бы использовать эту команду вам нужно заполнить аргументы в таком формате: /sclear [ссылка/упоминание]",
                cancellationToken);
            return;
        }

        if (targetId is not null &&
            !await ModerationCommandAccess.CanTargetAsync(
                api, store, context, administratorIds, targetId.Value, cancellationToken))
        {
            await context.SendMessageAsync(
                api,
                "Вы не можете применить это действие к данному пользователю, так как его должность выше или равна вашей.",
                cancellationToken);
            return;
        }

        var ids = new HashSet<long>();
        if (context.ReplyToConversationMessageId is > 0 &&
            context.ReplyToConversationMessageId != context.ConversationMessageId)
        {
            ids.Add(context.ReplyToConversationMessageId.Value);
        }

        var history = await api.GetRecentMessagesAsync(context.PeerId, Math.Min(100, count * 4 + 10), cancellationToken);
        foreach (var item in history)
        {
            if (ids.Count >= count ||
                item.ConversationMessageId == context.ConversationMessageId ||
                item.FromId <= 0 ||
                targetId is not null && item.FromId != targetId.Value)
            {
                continue;
            }

            ids.Add(item.ConversationMessageId);
        }

        var deleted = await api.DeleteConversationMessagesAsync(
            context.PeerId, ids.Take(count).ToArray(), cancellationToken);
        if (silent)
        {
            return;
        }

        var noun = deleted % 10 == 1 && deleted % 100 != 11
            ? "сообщение"
            : deleted % 10 is >= 2 and <= 4 && deleted % 100 is not (>= 12 and <= 14)
                ? "сообщения"
                : "сообщений";
        var response = deleted == 0
            ? "Не найдено сообщений для удаления."
            : targetId is { } userId
                ? $"🧹 {deleted} {noun} было удалено от пользователя {ModerationCommandAccess.FormatMention(userId, "пользователя")} " +
                  $"модератором {ModerationCommandAccess.FormatMention(context.UserId, "модератором")}."
                : $"🧹 {deleted} {noun} было удалено модератором {ModerationCommandAccess.FormatMention(context.UserId, "модератором")}.";
        await context.SendMessageAsync(api, response, cancellationToken, quoteCommand: false);
    }
}
