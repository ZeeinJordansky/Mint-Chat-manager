using System.Text.Json;

namespace MintChatManager.Commands;

public sealed class TicketRateCommand(
    PostgresStore store,
    long? supportChatPeerId,
    IReadOnlySet<long> administratorIds)
    : IBotCommand, IInternalBotCommand
{
    public string Name => "/ticket_rate";
    public string Description => "Оценить ответ поддержки";

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var parts = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !long.TryParse(parts[0], out var ticketId) ||
            ticketId <= 0 ||
            !short.TryParse(parts[1], out var rating) ||
            rating is < 1 or > 5)
        {
            await context.SendMessageAsync(api, "Не удалось определить оценку тикета.", cancellationToken);
            return;
        }

        var ticket = await store.GetSupportTicketAsync(ticketId, cancellationToken);
        if (ticket is null || ticket.UserId != context.UserId || ticket.Status != "closed")
        {
            await context.SendMessageAsync(api, "Тикет не найден!", cancellationToken);
            return;
        }

        if (!await store.RateSupportTicketAsync(ticketId, context.UserId, rating, cancellationToken))
        {
            await context.SendMessageAsync(api, "Вы уже оценили этот тикет!", cancellationToken);
            return;
        }

        var originalMessage =
            $"[id{ticket.AgentId ?? context.UserId}|Агент Поддержки] закрыл ваш тикет №{ticketId}.\n\n" +
            $"| Причина: {ticket.CloseReason}\n\n" +
            "Также вы можете оценить работу агента поддержки который вам помог нажав на кнопку.";
        await context.EditMessageAsync(
            api,
            originalMessage,
            JsonSerializer.Serialize(new { inline = true, buttons = Array.Empty<object>() }),
            cancellationToken);
        await context.SendMessageAsync(
            api,
            $"Вы оценили работу агента поддержки на {rating} {GetStarWord(rating)}.",
            cancellationToken);

        if (ticket.AgentId is { } agentId)
        {
            try
            {
                await SupportTicketNotifier.SendAsync(
                    api,
                    supportChatPeerId,
                    administratorIds,
                    $"Работа [id{agentId}|агента поддержки] над тикетом №{ticketId} была оценена в " +
                    $"{rating} {GetStarWord(rating)} [id{context.UserId}|пользователем].",
                    keyboard: null,
                    cancellationToken);
            }
            catch (SupportTicketNotificationException exception)
            {
                Console.Error.WriteLine($"Rating for ticket {ticketId} was saved but staff notification failed: {exception}");
            }
        }
    }

    private static string GetStarWord(short rating) => rating switch
    {
        1 => "звезду",
        2 or 3 or 4 => "звезды",
        _ => "звёзд"
    };
}
