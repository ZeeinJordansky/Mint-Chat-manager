namespace MintChatManager.Commands;

public sealed class BuyBitcoinCommand(PostgresStore store) : IBotCommand
{
    public string Name => "/buybtc";
    public string Description => "Купить Bitcoin за игровые деньги";
    public IReadOnlyCollection<string> Aliases => new[]
    {
        "/buyb", "/купитьbtc", "/купитьбиткоин", "/купитьбиткоины", "/купитькоин", "/buycoin"
    };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var account = await store.GetGameAccountAsync(context.UserId, cancellationToken);
        var price = BitcoinMarket.GetPrice();
        var input = arguments.Trim();
        var count = string.Equals(input, "все", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(input, "всё", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(input, "all", StringComparison.OrdinalIgnoreCase)
            ? account.WalletBalance / price
            : GameText.TryParseAmount(input, out var parsed) ? parsed : 1;
        if (count <= 0)
        {
            await context.SendMessageAsync(api, "Укажите количество!", cancellationToken);
            return;
        }

        var totalCost = checked(count * price);
        if (account.WalletBalance < totalCost)
        {
            await context.SendMessageAsync(
                api,
                $"У вас недостаточно средств! (Нужно: {GameText.FormatNumber(totalCost)}$)",
                cancellationToken);
            return;
        }

        if (!await store.BuyBitcoinAsync(context.UserId, count, price, cancellationToken))
        {
            await context.SendMessageAsync(
                api,
                $"У вас недостаточно средств! (Нужно: {GameText.FormatNumber(totalCost)}$)",
                cancellationToken);
            return;
        }

        await context.SendMessageAsync(
            api,
            $"{GameText.FormatMention(context.UserId)} купил(-а) {count} биткоинов за сумму {GameText.FormatNumber(totalCost)}$.",
            cancellationToken);
    }
}
