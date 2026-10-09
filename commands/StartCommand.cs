namespace MintChatManager.Commands;

public sealed class StartCommand(
    PostgresStore store,
    IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/start";
    public decimal MinimumRole => 6;
    public string Description => "Активировать чат-менеджера в беседе";
    public IReadOnlyCollection<string> Aliases =>
        new[] { "/старт", "/активировать", "/активация", "/включитьбота", "/activate", "/начать" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (await store.GetChatSettingsAsync(context.PeerId, cancellationToken) is { IsActive: true })
        {
            await context.SendMessageAsync(api, "Чат-менеджер в этой беседе уже активирован.", cancellationToken);
            return;
        }

        var canActivate = administratorIds.Contains(context.UserId);
        if (ModerationCommandAccess.IsConversation(context.PeerId))
        {
            var members = await api.GetConversationMembersAsync(context.PeerId, cancellationToken);
            canActivate |= members.Any(member =>
                member.UserId == context.UserId && (member.IsAdmin || member.IsOwner));
            if (!canActivate)
            {
                await context.SendMessageAsync(
                    api,
                    "Вы не можете активировать чат-менеджера в этой беседе так как вы не являетесь системным администратором или владельцем беседы.",
                    cancellationToken);
                return;
            }
        }

        await store.UpdateChatSettingsAsync(context.PeerId, "active", bool.TrueString, cancellationToken);
        var keyboard = GameText.CreateCallbackKeyboard("Команды", "/help", "positive");
        await context.SendMessageWithKeyboardAsync(
            api,
            $"✅ Беседа активирована [id{context.UserId}|пользователем].\n\n" +
            "Чтобы посмотреть команды, напишите /help или нажмите кнопку ниже.",
            keyboard,
            cancellationToken,
            quoteCommand: false);
    }
}
