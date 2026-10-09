namespace MintChatManager.Commands;

public sealed class ConfirmTransferCommand(PostgresStore store) : IBotCommand, IInternalBotCommand
{
    public string Name => "/transferconfirm";
    public string Description => "Подтвердить перевод денег";

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 ||
            !long.TryParse(parts[0], out var authorId) ||
            !long.TryParse(parts[1], out var recipientId) ||
            !long.TryParse(parts[2], out var amount) ||
            authorId != context.UserId ||
            recipientId <= 0 ||
            amount <= 0)
        {
            await context.SendMessageAsync(
                api,
                "🔒 Подтвердить перевод может только автор команды.",
                cancellationToken);
            return;
        }

        var transferred = await store.TransferMoneyAsync(authorId, recipientId, amount, cancellationToken);
        var result = transferred
            ? $"{GameText.FormatMention(authorId)} передал(-а) {GameText.FormatNumber(amount)}$ пользователю {GameText.FormatMention(recipientId)}"
            : "У вас недостаточно средств на руках!";
        await context.EditMessageAsync(api, result, """{"inline":true,"buttons":[]}""", cancellationToken);
    }
}
