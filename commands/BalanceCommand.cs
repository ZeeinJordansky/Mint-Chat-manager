namespace MintChatManager.Commands;

public sealed class BalanceCommand(PostgresStore store) : IBotCommand
{
    public string Name => "/баланс";
    public string Description => "Показать игровой баланс";
    public IReadOnlyCollection<string> Aliases => new[] { "/balance", "/б", "/bal" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var (parsedTargetId, _) = CommandTargetParser.Resolve(context, arguments);
        var targetId = parsedTargetId ?? context.UserId;
        if (targetId <= 0)
        {
            await context.SendMessageAsync(api, "Укажите корректный ID или ответьте на сообщение игрока.", cancellationToken);
            return;
        }

        var account = await store.GetGameAccountAsync(targetId, cancellationToken);
        var btcValue = checked(account.Bitcoin * BitcoinMarket.GetPrice());
        var total = checked(account.WalletBalance + account.BankBalance + btcValue);
        var reply =
            $"💳 Финансовый счёт пользователя {GameText.FormatMention(targetId)}\n\n" +
            $"💵 Наличные: {GameText.FormatNumber(account.WalletBalance)}$\n" +
            $"🏦 Банк: {GameText.FormatNumber(account.BankBalance)}$\n" +
            $"₿ Bitcoin: {account.Bitcoin} BTC (~{GameText.FormatNumber(btcValue)}$)\n\n" +
            $"💰 Общий баланс: {GameText.FormatNumber(total)}$";
        await context.SendMessageAsync(api, reply, cancellationToken);
    }
}
