namespace MintChatManager.Commands;

public sealed class TransferMoneyCommand(PostgresStore store) : IBotCommand
{
    public string Name => "/передать";
    public string Description => "Передать деньги другому игроку";
    public IReadOnlyCollection<string> Aliases => new[] { "/pay", "/transfer" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var (targetId, amountText) = CommandTargetParser.Resolve(context, arguments);

        if (targetId is null || targetId <= 0 || targetId == context.UserId)
        {
            await context.SendMessageAsync(
                api,
                $"👉🏻 Для того что бы использовать эту команду вам нужно заполнить аргументы в таком формате: {Name} [ссылка/упоминание]",
                cancellationToken);
            return;
        }

        var amountToken = amountText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(token => GameText.TryParseAmount(token, out _));
        if (amountToken is null || !GameText.TryParseAmount(amountToken, out var amount))
        {
            await context.SendMessageAsync(api, "Укажите корректную сумму для передачи!", cancellationToken);
            return;
        }

        var account = await store.GetGameAccountAsync(context.UserId, cancellationToken);
        if (account.WalletBalance < amount)
        {
            await context.SendMessageAsync(api, "У вас недостаточно средств на руках!", cancellationToken);
            return;
        }

        var keyboard = GameText.CreateCallbackKeyboard(
            "Передать",
            $"/transferconfirm {context.UserId} {targetId} {amount}",
            "positive",
            "Не передавать",
            $"/transfercancel {context.UserId}",
            "negative");
        await context.SendMessageWithKeyboardAsync(
            api,
            $"Вы собираетесь передать {GameText.FormatNumber(amount)}$ {GameText.FormatMention(targetId.Value)}\n| Для подтверждения нажмите на кнопку:",
            keyboard,
            cancellationToken);
    }
}
