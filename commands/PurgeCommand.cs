namespace MintChatManager.Commands;

public sealed class PurgeCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    private const int MaximumHistoryCount = 200;
    private const int DeleteBatchSize = 100;
    private static readonly HashSet<string> ModerationTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        "mute", "мут", "unmute", "размут", "warn", "варн", "unwarn", "разварн",
        "kick", "кик", "исключить", "выгнать", "ban", "бан", "unban", "разбан",
        "clear", "mclear", "очистить", "чистка", "purge", "пурдж", "pin", "пин",
        "unpin", "анпин", "открепить", "закрепить", "addaccesslevel", "removerole",
        "выдатьуровень", "снятьроль", "smute", "skick", "sban", "sunban"
    };

    public string Name => "/purge";
    public string Description => "Удалить последние команды модерации из беседы";
    public decimal MinimumRole => 3;
    public IReadOnlyCollection<string> Aliases => new[] { "/пурдж", "/чистка" };

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

        if (!TryParseCount(arguments, out var count))
        {
            await context.SendMessageAsync(
                api, "Укажите положительное число сообщений (максимум 200).", cancellationToken);
            return;
        }

        var history = await api.GetRecentMessagesAsync(context.PeerId, count, cancellationToken);
        var commandIds = history
            .Where(message => message.FromId > 0 && IsModerationCommand(message.Text))
            .Select(message => message.ConversationMessageId)
            .Distinct()
            .ToArray();
        var deleted = 0;
        foreach (var batch in commandIds.Chunk(DeleteBatchSize))
        {
            deleted += await api.DeleteConversationMessagesAsync(
                context.PeerId, batch, cancellationToken);
        }

        var response = deleted == 0
            ? "Не найдено сообщений команд для очистки."
            : $"🧹 Очистка завершена. Удалено {deleted} сообщений с командами.";
        await context.SendMessageAsync(api, response, cancellationToken, quoteCommand: false);
    }

    private static bool TryParseCount(string arguments, out int count)
    {
        count = 0;
        var firstArgument = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (firstArgument is null)
        {
            count = MaximumHistoryCount;
            return true;
        }

        if (!int.TryParse(firstArgument, out var parsedCount) || parsedCount <= 0)
        {
            return false;
        }

        count = Math.Clamp(parsedCount, 1, MaximumHistoryCount);
        return true;
    }

    private static bool IsModerationCommand(string text)
    {
        var content = text.Trim();
        if (content.StartsWith('['))
        {
            var mentionEnd = content.IndexOf(']');
            if (mentionEnd >= 0)
            {
                content = content[(mentionEnd + 1)..].TrimStart();
            }
        }

        if (content.Length == 0 || !"/!+.~?:;".Contains(content[0]))
        {
            return false;
        }

        var firstWord = content.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (firstWord is null)
        {
            return false;
        }

        return ModerationTriggers.Contains(firstWord.TrimStart('/', '!', '.', '+', '~', '?', ':', ';'));
    }
}
