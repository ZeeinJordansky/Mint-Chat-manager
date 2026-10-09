namespace MintChatManager.Commands;

public sealed class SilentKickCommand(
    PostgresStore store,
    IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/skick";
    public string Description => "Исключить участника без ответа в чате";
    public decimal MinimumRole => 1;
    public IReadOnlyCollection<string> Aliases => new[] { "/скик", "/тихийкик" };

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

        if (!await ModerationCommandAccess.CanModerateAsync(
                api, store, context, administratorIds, requiredRole: 1, cancellationToken))
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        var (targetId, _) = CommandTargetParser.Resolve(context, arguments);
        if (targetId is null)
        {
            await context.SendMessageAsync(
                api,
                $"👉🏻 Для того что бы использовать эту команду вам нужно заполнить аргументы в таком формате: {Name} [ссылка/упоминание]",
                cancellationToken);
            return;
        }

        if (targetId == context.UserId)
        {
            await context.SendMessageAsync(api, "Вы не можете исключить самого себя!", cancellationToken);
            return;
        }

        if (!await ModerationCommandAccess.CanTargetAsync(
                api, store, context, administratorIds, targetId.Value, cancellationToken))
        {
            await context.SendMessageAsync(
                api,
                "Вы не можете применить это действие к данному пользователю, так как его должность выше или равна вашей.",
                cancellationToken);
            return;
        }

        var ownerId = await api.GetConversationOwnerIdAsync(context.PeerId, cancellationToken);
        var targetRole = await ModerationCommandAccess.GetEffectiveRoleAsync(
            api, store, context.PeerId, targetId.Value, administratorIds, cancellationToken);
        if (ownerId == targetId || targetRole >= 6)
        {
            await context.SendMessageAsync(api, "Вы не можете исключить администратора беседы!", cancellationToken);
            return;
        }

        await api.RemoveChatUserAsync(context.PeerId, targetId.Value, cancellationToken);
        await store.AddModerationActionAsync(
            context.PeerId, context.UserId, targetId.Value, "kick", "без причины", null, cancellationToken);
    }
}
