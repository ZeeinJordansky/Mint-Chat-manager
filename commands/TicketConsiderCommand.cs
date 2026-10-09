using System.Text.Json;

namespace MintChatManager.Commands;

public sealed class TicketConsiderCommand(
    PostgresStore store,
    IReadOnlySet<long> administratorIds) : IBotCommand, IInternalBotCommand
{
    public string Name => "/ticket_consider";
    public string Description => "Взять тикет на рассмотрение";
    public decimal MinimumRole => 7.4m;

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (await store.GetGlobalRoleAsync(context.UserId, administratorIds, cancellationToken) < MinimumRole)
        {
            await context.SendMessageAsync(
                api, "Эта кнопка доступна с Агента Поддержки!", cancellationToken);
            return;
        }

        if (!long.TryParse(arguments.Trim(), out var ticketId) || ticketId <= 0)
        {
            await context.SendMessageAsync(api, "Не удалось определить тикет.", cancellationToken);
            return;
        }

        var ticket = await store.GetSupportTicketAsync(ticketId, cancellationToken);
        if (ticket is null)
        {
            await context.SendMessageAsync(api, "Тикет не найден!", cancellationToken);
            return;
        }

        if (ticket.Status != "open" ||
            !await store.StartSupportTicketAsync(ticketId, context.UserId, cancellationToken))
        {
            await context.SendMessageAsync(
                api, "Тикет уже рассматривается или закрыт!", cancellationToken);
            return;
        }

        var message =
            $"Поступил новый тикет №{ticketId} на рассмотрение!\n\n" +
            $"| Пользователь, который подал тикет - [id{ticket.UserId}|{ticket.UserName}]\n\n" +
            $"| Текст тикета:\n{ticket.Text}";
        await context.EditMessageAsync(
            api, message, JsonSerializer.Serialize(new { inline = true, buttons = Array.Empty<object>() }),
            cancellationToken);
        await context.SendMessageAsync(
            api,
            $"Тикет от {ModerationCommandAccess.FormatMention(ticket.UserId, "пользователя")} " +
            $"взят на рассмотрение [id{context.UserId}|агентом поддержки]",
            cancellationToken);

        try
        {
            await api.SendMessageAsync(
                ticket.UserId,
                $"Ваш тикет №{ticketId} был взят на рассмотрение " +
                $"[id{context.UserId}|Агентом Поддержки Чат-менеджера], ожидайте ответа.",
                cancellationToken);
        }
        catch (VkApiException exception)
        {
            Console.Error.WriteLine($"Ticket {ticketId} was assigned but the user could not be notified: {exception}");
        }
    }
}
