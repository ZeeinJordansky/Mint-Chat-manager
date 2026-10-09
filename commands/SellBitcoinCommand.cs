namespace MintChatManager.Commands;

public sealed class SellBitcoinCommand(PostgresStore store) : IBotCommand
{
    public string Name => "/sellbtc";
    public string Description => "Продать Bitcoin за игровые деньги";
    public IReadOnlyCollection<string> Aliases => new[]
    {
        "/sellb", "/продатьbtc", "/продатьбиткоин", "/продатьбиткоины", "/продатькоин", "/sellcoin"
    };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var account = await store.GetGameAccountAsync(context.UserId, cancellationToken);
        var input = arguments.Trim();
        var count = string.Equals(input, "все", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(input, "всё", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(input, "all", StringComparison.OrdinalIgnoreCase)
            ? account.Bitcoin
            : GameText.TryParseAmount(input, out var parsed) ? parsed : 1;
        if (count <= 0 || account.Bitcoin < count)
        {
            await context.SendMessageAsync(
                api,
                $"У вас недостаточно биткоинов! (Имеется: {account.Bitcoin} BTC)",
                cancellationToken);
            return;
        }

        var price = BitcoinMarket.GetPrice();
        var totalRefund = checked(count * price);
        if (await store.SellBitcoinAsync(context.UserId, count, price, cancellationToken) is null)
        {
            await context.SendMessageAsync(
                api,
                $"У вас недостаточно биткоинов! (Имеется: {account.Bitcoin} BTC)",
                cancellationToken);
            return;
        }

        await context.SendMessageAsync(
            api,
            $"{GameText.FormatMention(context.UserId)} продал(-а) {count} биткоинов за сумму {GameText.FormatNumber(totalRefund)}$.",
            cancellationToken);
    }
}
