namespace MintChatManager.Commands;

public sealed class RemoveRoleCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/removerole";
    public string Description => "Снять уровень прав у участника";
    public decimal MinimumRole => 1;
    public IReadOnlyCollection<string> Aliases => new[]
    {
        "/rrole", "/delrole", "/dellevel", "/removeaccesslevel", "/rlevel", "/removelevel",
        "/снятьроль", "/снятьправа", "/унроль", "/unrole", "/delmoder", "/delsenmoder",
        "/deladmin", "/delsenadmin", "/delzsa", "/delsa", "/unmoder", "/arrole", "/grrole"
    };

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

        var authorRole = await ModerationCommandAccess.GetEffectiveRoleAsync(
            api, store, context.PeerId, context.UserId, administratorIds, cancellationToken);
        var isAdmin = administratorIds.Contains(context.UserId);
        if (authorRole < 1 && !isAdmin)
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
            await context.SendMessageAsync(api, "Вы не можете снять должность самому себе!", cancellationToken);
            return;
        }

        var targetRole = await ModerationCommandAccess.GetEffectiveRoleAsync(
            api, store, context.PeerId, targetId.Value, administratorIds, cancellationToken);
        if (targetRole == 0)
        {
            await context.SendMessageAsync(api, "У пользователя нет назначенных ролей!", cancellationToken);
            return;
        }

        if (!isAdmin && authorRole <= targetRole)
        {
            await context.SendMessageAsync(
                api,
                "Вы не можете применить это действие к данному пользователю, так как его должность выше или равна вашей.",
                cancellationToken);
            return;
        }

        var isGlobalRole = targetRole is >= 1 and < 8 && authorRole >= 8;
        await store.SetRoleAsync(
            context.PeerId,
            targetId.Value,
            0,
            global: isGlobalRole,
            context.UserId,
            cancellationToken);
        await store.SetRoleAsync(
            context.PeerId,
            targetId.Value,
            0,
            global: false,
            context.UserId,
            cancellationToken);

        await context.SendMessageAsync(
            api,
            $"у {ModerationCommandAccess.FormatMention(targetId.Value, "пользователя")} был снят уровень прав " +
            $"{ModerationCommandAccess.FormatMention(context.UserId, "модератором")}.",
            cancellationToken);
    }
}
