namespace MintChatManager.Commands;

public sealed class AddAccessLevelCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/addaccesslevel";
    public string Description => "Назначить уровень прав участнику беседы";
    public decimal MinimumRole => 2;
    public IReadOnlyCollection<string> Aliases => new[]
    {
        "/addlevel", "/setlevel", "/setaccesslevel", "/addaccess", "/сетлевел", "/выдатьуровень", "/выдатьправа"
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
        if (authorRole < 2 && !isAdmin)
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        var (targetId, roleText) = CommandTargetParser.Resolve(context, arguments);
        if (targetId is null)
        {
            await context.SendMessageAsync(
                api,
                $"👉🏻 Для того что бы использовать эту команду вам нужно заполнить аргументы в таком формате: {Name} [ссылка/упоминание]",
                cancellationToken);
            return;
        }

        var (targetRole, roleName) = ParseRole(roleText);
        if (targetRole == 0)
        {
            await context.SendMessageAsync(
                api,
                "Укажите уровень или название роли! Допустимые уровни:\n1 - Модератор\n2 - Старший Модератор\n3 - Администратор\n4 - Главный Администратор\n5 - Руководитель беседы",
                cancellationToken);
            return;
        }

        var requiredRole = targetRole == 6 ? 7 : targetRole + 1;
        if (authorRole < requiredRole && !isAdmin)
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
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

        await store.SetRoleAsync(
            context.PeerId,
            targetId.Value,
            targetRole,
            global: false,
            context.UserId,
            cancellationToken);
        var message =
            $"{ModerationCommandAccess.FormatMention(targetId.Value, "Пользователю")} выдан уровень прав «{roleName}» " +
            $"{ModerationCommandAccess.FormatMention(context.UserId, "модератором")}.";
        var keyboard = GameText.CreateCallbackKeyboard(
            "Снять уровень прав",
            $"/removerole {targetId.Value}",
            "negative",
            "Список пользователей с уровнем прав",
            $"/staff {targetRole}",
            "secondary");
        await api.SendMessageWithKeyboardAsync(context.PeerId, message, keyboard, cancellationToken);
    }

    private static (decimal Role, string Name) ParseRole(string roleText)
    {
        var role = roleText.Trim().ToLowerInvariant();
        if (role == "1" || role.Contains("модератор") && !role.Contains("старш") || role == "модер")
            return (1, "Модератор");
        if (role == "2" || role.Contains("старший модератор") || role.Contains("ст модератор") ||
            role.Contains("стмодератор") || role is "смодер" or "стмодер")
            return (2, "Старший Модератор");
        if (role == "3" || role.Contains("администратор") && !role.Contains("главный") || role == "админ")
            return (3, "Администратор");
        if (role == "4" || role.Contains("главный администратор") || role.Contains("главадм") ||
            role == "га" || role.Contains("глав адм"))
            return (4, "Главный Администратор");
        if (role == "5" || role.Contains("руководитель"))
            return (5, "Руководитель беседы");
        if (role == "6" || role.Contains("владелец") || role == "овнер")
            return (6, "Владелец беседы");
        return (0, "");
    }
}
