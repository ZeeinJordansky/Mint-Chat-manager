namespace MintChatManager.Commands;

public sealed class CancelTransferCommand : IBotCommand, IInternalBotCommand
{
    public string Name => "/transfercancel";
    public string Description => "Отменить перевод денег";

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
                "🔒 Отменить перевод может только автор команды.",
                cancellationToken);
            return;
        }

        await context.EditMessageAsync(
            api,
            "Перевод отменён.",
            """{"inline":true,"buttons":[]}""",
            cancellationToken);
    }
}
