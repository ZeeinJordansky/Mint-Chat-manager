namespace MintChatManager.Commands;

public sealed class BankWithdrawCommand(PostgresStore store) : IBotCommand
{
    public string Name => "/снятьбанк";
    public string Description => "Снять деньги с банковского счёта";

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var account = await store.GetGameAccountAsync(context.UserId, cancellationToken);
        var amountText = arguments.Trim();
        var amount = amountText.Equals("все", StringComparison.OrdinalIgnoreCase)
            ? account.BankBalance
            : GameText.TryParseAmount(amountText, out var parsed) ? parsed : 0;
        if (amount <= 0)
        {
            await context.SendMessageAsync(api, "Укажите сумму!", cancellationToken);
            return;
        }

        var updated = await store.MoveMoneyToOrFromBankAsync(
            context.UserId,
            amount,
            deposit: false,
            cancellationToken);
        if (updated is null)
        {
            await context.SendMessageAsync(api, "У вас недостаточно средств в банке!", cancellationToken);
            return;
        }

        await context.SendMessageAsync(
            api,
            $"Вы сняли {GameText.FormatNumber(amount)}$ с банка",
            cancellationToken);
    }
}
