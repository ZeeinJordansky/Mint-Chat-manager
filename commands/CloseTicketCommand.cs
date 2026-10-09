using System.Text.Json;

namespace MintChatManager.Commands;

public sealed class CloseTicketCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/closeticket";
    public string Description => "Закрыть тикет поддержки";
    public decimal MinimumRole => 7.4m;
    public IReadOnlyCollection<string> Aliases =>
        new[] { "/закрыть", "/close", "/end", "/solve", "/решено" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var role = await store.GetGlobalRoleAsync(context.UserId, administratorIds, cancellationToken);
        if (role < MinimumRole)
        {
            return;
        }

        var parts = arguments.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !long.TryParse(parts[0], out var ticketId) || ticketId <= 0)
        {
            await context.SendMessageAsync(
                api, "Использование: /closeticket [id] [причина]", cancellationToken);
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

        if (ticket.AgentId is { } agentId && agentId != context.UserId && role < 10)
        {
            await context.SendMessageAsync(
                api, "Вы не являетесь ответственным за этот тикет!", cancellationToken);
            return;
        }

        var reason = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
            ? parts[1].Trim()
            : "Вопрос решён";
        if (!await store.CloseSupportTicketAsync(
                ticketId, context.UserId, role, reason, cancellationToken))
        {
            await context.SendMessageAsync(api, "Этот тикет уже закрыт!", cancellationToken);
            return;
        }

        var closedMessage =
            $"[id{context.UserId}|Агент Поддержки] закрыл ваш тикет №{ticketId}.\n" +
            $"| Причина: {reason}\n" +
            "Также вы можете оценить работу агента поддержки который вам помог нажав на кнопку.";
        try
        {
            await api.SendMessageWithKeyboardAsync(
                ticket.UserId,
                closedMessage,
                CreateRatingKeyboard(ticketId),
                cancellationToken);
        }
        catch (VkApiException exception)
        {
            Console.Error.WriteLine($"Ticket {ticketId} was closed but the user could not be notified: {exception}");
            await context.SendMessageAsync(
                api, "Тикет закрыт, но не удалось уведомить пользователя.", cancellationToken);
            return;
        }

        await context.SendMessageAsync(api, $"Тикет №{ticketId} закрыт.", cancellationToken);
    }

    private static string CreateRatingKeyboard(long ticketId)
    {
        var buttons = new[]
        {
            new[] { CreateRatingButton(ticketId, 5), CreateRatingButton(ticketId, 4), CreateRatingButton(ticketId, 3) },
            new[] { CreateRatingButton(ticketId, 2), CreateRatingButton(ticketId, 1) }
        };
        return JsonSerializer.Serialize(new { inline = true, buttons });
    }

    private static object CreateRatingButton(long ticketId, int stars) => new
    {
        action = new
        {
            type = "callback",
            label = $"{stars} {(stars == 5 ? "звёзд" : "звезды")}",
            payload = new { cmd = "ticket_rate", tId = ticketId, stars }
        },
        color = "secondary"
    };
}
