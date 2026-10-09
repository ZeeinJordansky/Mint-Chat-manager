namespace MintChatManager.Commands;

public sealed class DailyBonusCommand(PostgresStore store) : IBotCommand
{
    public string Name => "/ежедневный";
    public string Description => "Забрать ежедневный игровой бонус";
    public IReadOnlyCollection<string> Aliases => new[] { "/ежедневный_бонус", "/бонус" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var account = await store.GetGameAccountAsync(context.UserId, cancellationToken);
        var day = account.DailyBonusDay;
        var label = day switch
        {
            1 => "25.000$",
            2 => "2 репутации",
            3 => "3 литра пива",
            4 => "75.000$",
            5 => "4 репутации",
            6 => "7 литров пива",
            7 => "300.000$",
            _ => throw new InvalidOperationException($"Invalid daily bonus day {day}.")
        };
        var ready = account.LastDailyClaimedAt is null ||
                    account.LastDailyClaimedAt <= DateTimeOffset.UtcNow.AddHours(-24);
        var status = ready ? "готов" : "не готов";
        var text = $"🎁 Ежедневный бонус: {status}\n| Бонус сегодня: {label}";
        if (!ready)
        {
            await context.SendMessageAsync(api, text, cancellationToken);
            return;
        }

        var keyboard = GameText.CreateCallbackKeyboard(
            "Забрать бонус",
            $"/dailyclaim {context.UserId}",
            "positive");
        await context.SendMessageWithKeyboardAsync(api, text, keyboard, cancellationToken);
    }
}
