namespace MintChatManager.Commands;

public sealed class StaffListCommand(PostgresStore store) : IBotCommand
{
    public string Name => "/staff";
    public string Description => "Показать модерационный состав беседы";
    public IReadOnlyCollection<string> Aliases => new[] { "/состав", "/стафф", "/руководство", "/модеры" };

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

        var staff = await store.GetChatStaffAsync(context.PeerId, cancellationToken);
        if (int.TryParse(arguments.Trim(), out var requestedRole))
        {
            staff = staff.Where(member => member.Role == requestedRole).ToArray();
        }
        var text = staff.Count == 0
            ? "В этой беседе пока нет пользователей с выданными уровнями прав."
            : string.Join(
                '\n',
                staff.Select(member =>
                    $"{ModerationCommandAccess.FormatMention(member.UserId, "Пользователь")} — {BotRoleNames.GetDisplayName(member.Role)}"));
        await context.SendMessageAsync(api, $"...::Состав беседы::...\n\n{text}", cancellationToken);
    }
}
