using System.Text.Json;

namespace MintChatManager.Commands;

public sealed class TicketCommand(
    PostgresStore store,
    long? supportChatPeerId,
    IReadOnlySet<long> administratorIds) : IBotCommand
{
    private const string SupportGroupLink = "https://vk.ru/write-239281784";

    public string Name => "/ticket";
    public string Description => "Отправить обращение в поддержку";
    public IReadOnlyCollection<string> Aliases => new[] { "/тикет", "/репорт", "/report" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (ModerationCommandAccess.IsConversation(context.PeerId) &&
            !await api.IsMessagesFromGroupAllowedAsync(context.UserId, cancellationToken))
        {
            await context.SendMessageWithKeyboardAsync(
                api,
                "Для начала работы с этой командой напишите в личные сообщения чат-менеджера.",
                CreateSupportLinkKeyboard(),
                cancellationToken);
            return;
        }

        var text = arguments.Trim();
        if (text.Length == 0)
        {
            var message =
                "Введите текст тикета!\n" +
                "Вы можете отправить его прямо здесь (например: /тикет ошибка в боте) или написать в личные сообщения бота.";
            await context.SendMessageWithKeyboardAsync(
                api, message, CreateSupportLinkKeyboard(), cancellationToken);
            return;
        }

        var userName = await api.GetSubjectNameAsync(context.UserId, cancellationToken)
            ?? $"User{context.UserId}";
        var ticketId = await store.CreateSupportTicketAsync(
            context.UserId, userName, text, cancellationToken);
        var keyboard = JsonSerializer.Serialize(new
        {
            inline = true,
            buttons = new[]
            {
                new[]
                {
                    new
                    {
                        action = new
                        {
                            type = "callback",
                            label = "Рассмотреть",
                            payload = new { cmd = "ticket_consider", tId = ticketId }
                        },
                        color = "positive"
                    }
                }
            }
        });
        var notification =
            $"Поступил новый тикет №{ticketId} на рассмотрение!\n" +
            $"| Пользователь, который подал тикет - [id{context.UserId}|{userName}]\n" +
            $"| Текст тикета:\n{text}";
        try
        {
            await SupportTicketNotifier.SendAsync(
                api, supportChatPeerId, administratorIds, notification, keyboard, cancellationToken);
        }
        catch (SupportTicketNotificationException exception)
        {
            Console.Error.WriteLine(
                $"Ticket {ticketId} was saved but support-chat notification failed: {exception}");
            await context.SendMessageAsync(
                api,
                $"Тикет №{ticketId} сохранён, но уведомление поддержки не отправлено. Сообщите этот номер администратору.",
                cancellationToken,
                disableMentions: true);
            return;
        }

        await context.SendMessageAsync(
            api,
            "Вы успешно подали тикет. Ответ придёт вам в личные сообщения чат-менеджера.",
            cancellationToken);
    }

    private static string CreateSupportLinkKeyboard() =>
        JsonSerializer.Serialize(new
        {
            inline = true,
            buttons = new[]
            {
                new[]
                {
                    new
                    {
                        action = new
                        {
                            type = "open_link",
                            label = "Написать в лс чат-менеджера",
                            link = SupportGroupLink
                        }
                    }
                }
            }
        });
}
