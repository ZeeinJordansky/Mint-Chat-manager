namespace MintChatManager;

internal static class BitcoinMarket
{
    private const long PriceIntervalMilliseconds = 10 * 60 * 60 * 1_000;

    public static long GetPrice()
    {
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / PriceIntervalMilliseconds;
        var hash = (bucket * 9_301 + 49_297) % 233_280;
        return 85_000_000 + hash * 20_000_000 / 233_280;
    }

    public static string GetCountdown()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nextChange = (now / PriceIntervalMilliseconds + 1) * PriceIntervalMilliseconds;
        var remainingSeconds = Math.Max(0, (nextChange - now) / 1_000);
        return $"{remainingSeconds / 3_600:00}:{remainingSeconds % 3_600 / 60:00}:{remainingSeconds % 60:00}";
    }
}
