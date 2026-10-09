namespace MintChatManager.Commands;

public sealed class ZovConfirmCommand : IBotCommand, IInternalBotCommand
{
    private const int MentionsPerMessage = 30;

    public string Name => "/zovconfirm";
    public string Description => "Подтвердить вызов участников";

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!context.IsCallback ||
            parts.Length != 3 ||
            !long.TryParse(parts[0], out var authorId) ||
            authorId != context.UserId ||
            parts[1] is not ("all" or "online"))
        {
            await context.SendMessageAsync(api, "🔒 Подтвердить вызов может только его автор.", cancellationToken);
            return;
        }

        string reason;
        try
        {
            var encoded = parts[2].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            reason = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            await context.SendMessageAsync(api, "Не удалось прочитать причину вызова.", cancellationToken);
            return;
        }

        var members = await api.GetConversationMembersAsync(context.PeerId, cancellationToken);
        var selected = members
            .Where(member => member.UserId > 0 &&
                             !member.IsDeactivated &&
                             (parts[1] == "all" || member.IsOnline || member.IsOnlineMobile))
            .ToArray();
        var pages = selected
            .Select((member, index) => new
            {
                Member = member,
                Page = index / MentionsPerMessage
            })
            .GroupBy(item => item.Page)
            .Select(group => group.Select(item => item.Member).ToArray())
            .ToArray();

        if (pages.Length == 0)
        {
            await context.EditMessageAsync(
                api,
                parts[1] == "online"
                    ? "Сейчас в сети нет участников для вызова."
                    : "В этой беседе нет участников для вызова.",
                """{"inline":true,"buttons":[]}""",
                cancellationToken);
            return;
        }

        var targetDescription = parts[1] == "online" ? "онлайн-участников" : "всех участников";
        for (var pageIndex = 0; pageIndex < pages.Length; pageIndex++)
        {
            var mentions = string.Join(
                ' ',
                pages[pageIndex].Select(member => $"[id{member.UserId}|участник]"));
            var message = pageIndex == 0
                ? $"📣 Вызов {targetDescription} от [id{authorId}|модератора]. Причина: {reason}\n\n{mentions}"
                : mentions;
            if (pageIndex == 0)
            {
                await context.EditMessageAsync(
                    api,
                    message,
                    """{"inline":true,"buttons":[]}""",
                    cancellationToken);
            }
            else
            {
                await context.SendMessageAsync(api, message, cancellationToken, quoteCommand: false);
            }
        }
    }
}
