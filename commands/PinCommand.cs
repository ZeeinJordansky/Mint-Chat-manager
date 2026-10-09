namespace MintChatManager.Commands;

public sealed class PinCommand(
    PostgresStore store,
    IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/pin";
    public string Description => "Закрепить сообщение в беседе";
    public decimal MinimumRole => 4;
    public IReadOnlyCollection<string> Aliases => new[] { "/пин", "/закр", "/закрепить" };

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

        var role = await ModerationCommandAccess.GetEffectiveRoleAsync(
            api, store, context.PeerId, context.UserId, administratorIds, cancellationToken);
        if (role < 4)
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        if (context.ReplyToConversationMessageId is not > 0)
        {
            await context.SendMessageAsync(api, "Ответьте на сообщение, которое нужно закрепить!", cancellationToken);
            return;
        }

        await api.PinConversationMessageAsync(
            context.PeerId, context.ReplyToConversationMessageId.Value, cancellationToken);
        var message = $"📌 Сообщение успешно закреплено в беседе {ModerationCommandAccess.FormatMention(context.UserId, "модератором")}.";
        if (role >= 5)
        {
            var keyboard = GameText.CreateCallbackKeyboard("Открепить сообщение", "/unpin", "negative");
            await api.SendMessageWithKeyboardAsync(context.PeerId, message, keyboard, cancellationToken);
            return;
        }

        await context.SendMessageAsync(api, message, cancellationToken);
    }
}
