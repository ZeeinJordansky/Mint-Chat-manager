namespace MintChatManager.Commands;

public sealed class BitcoinRateCommand : IBotCommand
{
    public string Name => "/btc";
    public string Description => "Показать текущий курс Bitcoin";
    public IReadOnlyCollection<string> Aliases => new[] { "/курс", "/btccourse", "/биткоин" };

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var text =
            $"...::Курс BTC::...\n\n| Текущая цена: {GameText.FormatNumber(BitcoinMarket.GetPrice())}$\n" +
            $"| Следующая смена цены: {BitcoinMarket.GetCountdown()}";
        return context.SendMessageAsync(api, text, cancellationToken);
    }
}
