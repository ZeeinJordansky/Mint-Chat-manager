using System.Globalization;
using System.Text;

namespace MintChatManager.Commands;

public sealed class ManagerStaffCommand(
    PostgresStore store,
    IReadOnlySet<long> administratorIds) : IBotCommand
{
    private static readonly Dictionary<int, string> RoleTitles = new()
    {
        [12] = "Владельцы Чат-менеджера",
        [11] = "Заместители Владельца чат-менеджера",
        [10] = "Главная модерация",
        [9] = "Старшая модерация",
        [8] = "Модерация",
        [7] = "Младшая модерация"
    };

    private static readonly HashSet<long> ExcludedUsers = new() { 1, 1_115_715_881 };

    public string Name => "/mstaff";
    public string Description => "Показать состав глобальной модерации";
    public decimal MinimumRole => 7;
    public IReadOnlyCollection<string> Aliases =>
        new[] { "/мстафф", "/модсостав", "/gstaff", "/гстафф", "/гсостав", "/gstaffs", "/составсети" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (await store.GetGlobalRoleAsync(context.UserId, administratorIds, cancellationToken) < MinimumRole)
        {
            await context.SendMessageAsync(
                api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        var members = await store.GetGlobalStaffAsync(cancellationToken);
        var byRole = RoleTitles.Keys.ToDictionary(role => role, _ => new List<(long Id, string Name)>());
        foreach (var administratorId in administratorIds)
        {
            if (!ExcludedUsers.Contains(administratorId))
            {
                byRole[12].Add((administratorId, await GetNameAsync(api, administratorId, cancellationToken)));
            }
        }

        foreach (var member in members)
        {
            if (member.Role != decimal.Truncate(member.Role))
            {
                continue;
            }

            var role = decimal.ToInt32(member.Role);
            if (!byRole.TryGetValue(role, out var roleMembers) ||
                ExcludedUsers.Contains(member.UserId) ||
                administratorIds.Contains(member.UserId))
            {
                continue;
            }

            roleMembers.Add((member.UserId, await GetNameAsync(api, member.UserId, cancellationToken)));
        }

        var text = new StringBuilder("...::Модерация чат-менеджера::...\n");
        foreach (var (role, title) in RoleTitles)
        {
            text.Append("| ").Append(title).Append(":\n");
            if (byRole[role].Count == 0)
            {
                text.Append("— Отсутствуют\n");
                continue;
            }

            foreach (var member in byRole[role])
            {
                text.Append("— [id").Append(member.Id).Append('|').Append(member.Name).Append("]\n");
            }
        }

        var messageId = await api.SendMessageAndGetIdAsync(
            context.PeerId,
            text.ToString().TrimEnd(),
            cancellationToken,
            replyToMessageId: context.MessageId,
            replyToConversationMessageId: context.ConversationMessageId,
            disableMentions: true)
            ?? throw new InvalidOperationException("VK returned no ID for the global staff list message.");
        _ = DeleteAfterDelayAsync(api, context.PeerId, messageId);
    }

    private static async Task<string> GetNameAsync(
        VkApiClient api,
        long userId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await api.GetSubjectNameAsync(userId, cancellationToken) ?? $"User{userId}";
        }
        catch (VkApiException exception)
        {
            Console.Error.WriteLine($"Could not resolve global moderator {userId}: {exception}");
            return $"User{userId.ToString(CultureInfo.InvariantCulture)}";
        }
    }

    private static async Task DeleteAfterDelayAsync(VkApiClient api, long peerId, long messageId)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            await api.DeleteMessageAsync(peerId, messageId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Failed to delete temporary global staff list: {exception}");
        }
    }
}
