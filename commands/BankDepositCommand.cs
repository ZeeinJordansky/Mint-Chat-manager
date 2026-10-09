namespace MintChatManager.Commands;

public sealed class BankDepositCommand(PostgresStore store) : IBotCommand
{
    public string Name => "/банк";
    public string Description => "Положить деньги в банк";

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var account = await store.GetGameAccountAsync(context.UserId, cancellationToken);
        var amountText = arguments.Trim();
        var amount = amountText.Equals("все", StringComparison.OrdinalIgnoreCase)
            ? account.WalletBalance
            : GameText.TryParseAmount(amountText, out var parsed) ? parsed : 0;
        if (amount <= 0)
        {
            await context.SendMessageAsync(api, "Укажите сумму!", cancellationToken);
            return;
        }

        var updated = await store.MoveMoneyToOrFromBankAsync(
            context.UserId,
            amount,
            deposit: true,
            cancellationToken);
        if (updated is null)
        {
            await context.SendMessageAsync(api, "У вас недостаточно средств!", cancellationToken);
            return;
        }

        await context.SendMessageAsync(
            api,
            $"Вы положили {GameText.FormatNumber(amount)}$ в банк",
            cancellationToken);
    }
}
