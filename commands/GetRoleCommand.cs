using System.Globalization;

namespace MintChatManager.Commands;

public sealed class GetRoleCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/getrole";
    public string Description => "Показать информацию о должности пользователя";
    public decimal MinimumRole => 1;
    public IReadOnlyCollection<string> Aliases => new[] { "/гетроль", "/инфороль", "/checkrole", "/чекроль" };

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

        var assignment = await store.GetRoleAssignmentAsync(context.PeerId, targetId.Value, cancellationToken);
        if (assignment is null || assignment.GlobalRole <= 0 && assignment.ChatRole <= 0)
        {
            await context.SendMessageAsync(
                api,
                $"...::Информация о выдаче уровне прав пользователю {ModerationCommandAccess.FormatMention(targetId.Value, "пользователю")}::...\n\n| У пользователя отсутствует уровень прав",
                cancellationToken);
            return;
        }

        var assignedBy = assignment.AssignedBy is > 0
            ? ModerationCommandAccess.FormatMention(assignment.AssignedBy.Value, "модератор")
            : "Неизвестно";
        var assignedAt = assignment.AssignedAt is { } date
            ? date.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : "Неизвестно";
        var role = assignment.ChatRole > 0 ? assignment.ChatRole : assignment.GlobalRole;
        var details = assignment.ChatRole > 0
            ? $"| Модератор, который выдал(-а) уровень прав - {assignedBy}\n| Дата выдачи: {assignedAt}"
            : $"| Глобальная должность: {BotRoleNames.GetDisplayName(role)}";
        var message =
            $"...::Информация о выдаче уровне прав пользователю {ModerationCommandAccess.FormatMention(targetId.Value, "пользователю")}::...\n\n" +
            $"| Должность: {BotRoleNames.GetDisplayName(role)}\n{details}";
        await context.SendMessageAsync(api, message, cancellationToken);
    }
}
