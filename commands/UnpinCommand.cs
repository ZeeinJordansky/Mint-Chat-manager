namespace MintChatManager.Commands;

public sealed class UnpinCommand(
    PostgresStore store,
    IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/unpin";
    public string Description => "Открепить сообщение в беседе";
    public decimal MinimumRole => 4;
    public IReadOnlyCollection<string> Aliases => new[] { "/открепить", "/анпин", "/унпин", "/откр" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (!ModerationCommandAccess.IsConversation(context.PeerId))
        {
            await context.SendMessageAsync(api, "Команда доступна только в беседах!", cancellationToken);
            return;
        }

        var role = await ModerationCommandAccess.GetEffectiveRoleAsync(
            api, store, context.PeerId, context.UserId, administratorIds, cancellationToken);
        if (role < 4)
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        await api.UnpinConversationMessageAsync(context.PeerId, cancellationToken);
        await context.SendMessageAsync(
            api,
            $"📌 Сообщение откреплено {ModerationCommandAccess.FormatMention(context.UserId, "модератором")}.",
            cancellationToken);
    }
}
