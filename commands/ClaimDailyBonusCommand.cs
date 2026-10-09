namespace MintChatManager.Commands;

public sealed class ClaimDailyBonusCommand(PostgresStore store) : IBotCommand, IInternalBotCommand
{
    public string Name => "/dailyclaim";
    public string Description => "Забрать бонус после подтверждения кнопкой";

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(arguments.Trim(), out var authorId) || authorId != context.UserId)
        {
            await context.SendMessageAsync(
                api,
                "Только вызвавший команду может забрать бонус!",
                cancellationToken);
            return;
        }

        var result = await store.ClaimDailyBonusAsync(context.UserId, cancellationToken);
        if (!result.IsClaimed)
        {
            await context.SendMessageAsync(api, "Бонус еще не готов!", cancellationToken);
            return;
        }

        await context.EditMessageAsync(
            api,
            $"🎁 {GameText.FormatMention(context.UserId)}, вы забрали ежедневный бонус!\n\n| Получено: {GameText.FormatDailyBonus(result)}",
            """{"inline":true,"buttons":[]}""",
            cancellationToken);
    }
}
