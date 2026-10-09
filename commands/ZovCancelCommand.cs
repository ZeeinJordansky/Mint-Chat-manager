namespace MintChatManager.Commands;

public sealed class ZovCancelCommand : IBotCommand, IInternalBotCommand
{
    public string Name => "/zovcancel";
    public string Description => "Отменить вызов участников";

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (!context.IsCallback ||
            !long.TryParse(arguments.Trim(), out var authorId) ||
            authorId != context.UserId)
        {
            await context.SendMessageAsync(api, "🔒 Отменить вызов может только его автор.", cancellationToken);
            return;
        }

        await context.EditMessageAsync(
            api,
            "Вызов участников отменён.",
            """{"inline":true,"buttons":[]}""",
            cancellationToken);
    }
}
