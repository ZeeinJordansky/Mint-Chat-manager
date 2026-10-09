using System.Globalization;

namespace MintChatManager;

internal static class ModerationDurationParser
{
    private static readonly HashSet<string> Minutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "m", "м", "мин", "минута", "минуты", "минут"
    };
    private static readonly HashSet<string> Hours = new(StringComparer.OrdinalIgnoreCase)
    {
        "h", "ч", "час", "часа", "часов"
    };
    private static readonly HashSet<string> Days = new(StringComparer.OrdinalIgnoreCase)
    {
        "d", "д", "дн", "день", "дня", "дней"
    };

    public static (TimeSpan Duration, string Display, string Reason) ParseMute(string arguments)
    {
        var tokens = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        var duration = TimeSpan.FromMinutes(30);
        var display = "30 минут";
        var durationIndex = -1;

        for (var index = 0; index < tokens.Count; index++)
        {
            if (!TryParseValue(tokens[index], out var value, out var unit))
            {
                continue;
            }

            var minutes = unit switch
            {
                "" or "m" or "м" or "мин" or "минута" or "минуты" or "минут" => value,
                "h" or "ч" or "час" or "часа" or "часов" => (long)value * 60,
                "d" or "д" or "дн" or "день" or "дня" or "дней" => (long)value * 24 * 60,
                "s" or "с" or "сек" or "секунда" or "секунды" or "секунд" => (value + 59) / 60,
                _ => 0
            };
            if (minutes <= 0)
            {
                continue;
            }

            minutes = Math.Clamp(minutes, 1, 1_000);
            duration = TimeSpan.FromMinutes(minutes);
            display = $"{minutes} {RussianPlural(minutes, "минута", "минуты", "минут")}";
            durationIndex = index;
            break;
        }

        if (durationIndex >= 0)
        {
            tokens.RemoveAt(durationIndex);
        }

        return (duration, display, tokens.Count == 0 ? "без причины" : string.Join(' ', tokens));
    }

    public static (TimeSpan? Duration, string Reason) ParseBan(string arguments)
    {
        var tokens = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count == 0)
        {
            return (null, "без причины");
        }

        var durationIndex = -1;
        var durationDays = 0;
        if (TryParseValue(tokens[0], out var firstValue, out var firstUnit) &&
            firstValue > 0 &&
            (firstUnit.Length == 0 || Days.Contains(firstUnit)))
        {
            durationIndex = 0;
            durationDays = firstValue;
        }
        else if (tokens.Count > 1 &&
                 TryParseValue(tokens[^1], out var lastValue, out var lastUnit) &&
                 lastValue > 0 &&
                 (lastUnit.Length == 0 || Days.Contains(lastUnit)))
        {
            durationIndex = tokens.Count - 1;
            durationDays = lastValue;
        }

        if (durationIndex < 0)
        {
            return (null, string.Join(' ', tokens));
        }

        durationDays = Math.Clamp(durationDays, 1, 24_855);
        tokens.RemoveAt(durationIndex);
        var duration = TimeSpan.FromDays(durationDays);
        return (duration, tokens.Count == 0 ? "без причины" : string.Join(' ', tokens));
    }

    private static bool TryParseValue(string value, out int number, out string unit)
    {
        number = 0;
        unit = "";
        var split = 0;
        while (split < value.Length && char.IsDigit(value[split]))
        {
            split++;
        }

        if (split == 0 ||
            !int.TryParse(value.AsSpan(0, split), NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            return false;
        }

        unit = value[split..].TrimEnd('.').ToLowerInvariant();
        return true;
    }

    private static string RussianPlural(long value, string one, string few, string many)
    {
        var lastTwo = value % 100;
        if (lastTwo is >= 11 and <= 14)
        {
            return many;
        }

        return (value % 10) switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many
        };
    }
}
