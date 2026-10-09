using System.Globalization;
using System.Text.Json;

namespace MintChatManager;

internal static class GameText
{
    private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("en-US");

    public static string FormatNumber(long value) => value.ToString("N0", NumberCulture);

    public static string FormatMention(long userId) => $"[id{userId}|Игрок]";

    public static string FormatDailyBonus(DailyBonusClaimResult bonus)
    {
        if (bonus.Money > 0)
        {
            return $"{FormatNumber(bonus.Money)}$";
        }

        if (bonus.Reputation > 0)
        {
            return $"{bonus.Reputation} репутации";
        }

        return $"{bonus.Beer} литров пива";
    }

    public static string CreateCallbackKeyboard(string label, string command, string color)
    {
        return JsonSerializer.Serialize(new
        {
            inline = true,
            buttons = new[]
            {
                new[]
                {
                    new
                    {
                        action = new
                        {
                            type = "callback",
                            label,
                            payload = new { command }
                        },
                        color
                    }
                }
            }
        });
    }

    public static string CreateCallbackKeyboard(
        string firstLabel,
        string firstCommand,
        string firstColor,
        string secondLabel,
        string secondCommand,
        string secondColor)
    {
        return JsonSerializer.Serialize(new
        {
            inline = true,
            buttons = new[]
            {
                new[]
                {
                    new
                    {
                        action = new { type = "callback", label = firstLabel, payload = new { command = firstCommand } },
                        color = firstColor
                    },
                    new
                    {
                        action = new { type = "callback", label = secondLabel, payload = new { command = secondCommand } },
                        color = secondColor
                    }
                }
            }
        });
    }

    public static bool TryParseAmount(string input, out long amount)
    {
        amount = 0;
        var value = input.Trim().Replace(",", ".", StringComparison.Ordinal);
        var multiplier = 1L;
        while (value.EndsWith('k') || value.EndsWith('K') ||
               value.EndsWith('к') || value.EndsWith('К'))
        {
            if (multiplier > long.MaxValue / 1_000)
            {
                return false;
            }

            multiplier *= 1_000;
            value = value[..^1];
        }

        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ||
            parsed <= 0)
        {
            return false;
        }

        var multiplied = parsed * multiplier;
        if (multiplied > long.MaxValue)
        {
            return false;
        }

        amount = decimal.ToInt64(decimal.Floor(multiplied));
        return amount > 0;
    }
}
