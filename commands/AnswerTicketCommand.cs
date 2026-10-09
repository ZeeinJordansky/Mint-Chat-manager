namespace MintChatManager.Commands;

public sealed class AnswerTicketCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/answerticket";
    public string Description => "Ответить на тикет поддержки";
    public decimal MinimumRole => 7.4m;
    public IReadOnlyCollection<string> Aliases =>
        new[] { "/ответить", "/ans", "/otv", "/reply", "/ответ", "/comment", "/коммент", "/комментарий" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (await store.GetGlobalRoleAsync(context.UserId, administratorIds, cancellationToken) < MinimumRole)
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        var parts = arguments.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 ||
            !long.TryParse(parts[0], out var ticketId) ||
            ticketId <= 0 ||
            string.IsNullOrWhiteSpace(parts[1]))
        {
            await context.SendMessageAsync(
                api, "Использование: /answerticket [id] [текст]", cancellationToken);
            return;
        }

        var ticket = await store.GetSupportTicketAsync(ticketId, cancellationToken);
        if (ticket is null)
        {
            await context.SendMessageAsync(api, "Тикет не найден!", cancellationToken);
            return;
        }

        if (ticket.Status == "closed")
        {
            await context.SendMessageAsync(api, "Этот тикет уже закрыт!", cancellationToken);
            return;
        }

        var role = await store.GetGlobalRoleAsync(context.UserId, administratorIds, cancellationToken);
        if (ticket.AgentId is { } agentId && agentId != context.UserId && role < 10)
        {
            await context.SendMessageAsync(
                api, "Вы не являетесь ответственным за этот тикет!", cancellationToken);
            return;
        }

        if (ticket.AgentId is null &&
            !await store.StartSupportTicketAsync(ticketId, context.UserId, cancellationToken))
        {
            await context.SendMessageAsync(
                api, "Тикет уже рассматривается или закрыт!", cancellationToken);
            return;
        }

        await store.UpdateSupportTicketReplyAsync(ticketId, cancellationToken);
        var userMessage =
            $"[id{context.UserId}|Агент Поддержки] добавил комментарий к вашему тикету №{ticketId}\n" +
            $"| Текст комментария:\n{parts[1]}";
        try
        {
            await api.SendMessageAsync(ticket.UserId, userMessage, cancellationToken);
        }
        catch (VkApiException exception)
        {
            Console.Error.WriteLine($"Failed to deliver a reply to ticket {ticketId}: {exception}");
            await context.SendMessageAsync(
                api,
                "Не удалось отправить сообщение пользователю (возможно, закрыты ЛС).",
                cancellationToken);
            return;
        }

        await context.SendMessageAsync(
            api,
            $"[id{context.UserId}|Агент Поддержки] добавил комментарий к тикету №{ticketId} от " +
            $"{ModerationCommandAccess.FormatMention(ticket.UserId, "пользователя")}\n| Текст комментария:\n{parts[1]}",
            cancellationToken,
            disableMentions: true);
    }
}
