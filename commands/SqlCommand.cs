using System.Text;

namespace MintChatManager.Commands;

public sealed class SqlCommand(PostgresStore store, IReadOnlySet<long> adminIds) : IBotCommand
{
    private const int MaxQueryLength = 1500;
    private const int MaxResponseLength = 3400;

    public string Name => "/sql";
    public string Description => "Выполнить SELECT-запрос к базе данных (только для администратора)";

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (!adminIds.Contains(context.UserId))
        {
            await api.SendMessageAsync(
                context.PeerId,
                "Команда доступна только администратору.",
                cancellationToken);
            return;
        }

        var query = arguments.Trim();
        if (query.Length == 0)
        {
            await api.SendMessageAsync(
                context.PeerId,
                "Укажите один SQL-запрос только для чтения. Пример: /sql SELECT COUNT(*) FROM user_profiles",
                cancellationToken);
            return;
        }

        var statement = query.AsSpan().TrimEnd();
        if (statement.Length > 0 && statement[^1] == ';')
        {
            statement = statement[..^1].TrimEnd();
        }

        if (statement.Length > MaxQueryLength ||
            statement.Contains(';') ||
            !StartsWithReadQuery(statement))
        {
            await api.SendMessageAsync(
                context.PeerId,
                "Разрешён только один SELECT- или WITH-запрос длиной до 1500 символов.",
                cancellationToken);
            return;
        }

        query = statement.ToString();
        try
        {
            var result = await store.ExecuteReadOnlyQueryAsync(query, cancellationToken);
            await api.SendMessageAsync(
                context.PeerId,
                FormatResult(result),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await api.SendMessageAsync(
                context.PeerId,
                $"Ошибка SQL: {exception.Message[..Math.Min(exception.Message.Length, 500)]}",
                cancellationToken);
        }
    }

    private static bool StartsWithReadQuery(ReadOnlySpan<char> query)
    {
        var trimmed = query.TrimStart();
        return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
               IsKeywordBoundary(trimmed, 6) ||
               trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase) &&
               IsKeywordBoundary(trimmed, 4);
    }

    private static bool IsKeywordBoundary(ReadOnlySpan<char> query, int keywordLength)
    {
        return query.Length == keywordLength || char.IsWhiteSpace(query[keywordLength]) || query[keywordLength] == '(';
    }

    private static string FormatResult(SqlQueryResult result)
    {
        var output = new StringBuilder("Результат SQL (только чтение):\n");
        output.AppendLine(string.Join(" | ", result.Columns));
        foreach (var row in result.Rows)
        {
            var line = string.Join(" | ", row.Select(value =>
                value.Replace('\r', ' ').Replace('\n', ' ')));
            if (output.Length + line.Length + Environment.NewLine.Length > MaxResponseLength)
            {
                output.AppendLine("... вывод ограничен");
                break;
            }

            output.AppendLine(line);
        }

        if (result.RowsTruncated)
        {
            output.AppendLine("... показаны первые 15 строк");
        }

        if (result.ColumnsTruncated)
        {
            output.AppendLine("... показаны первые 8 столбцов");
        }

        if (result.Rows.Count == 0)
        {
            output.AppendLine("(нет строк)");
        }

        return output.ToString().TrimEnd();
    }
}
