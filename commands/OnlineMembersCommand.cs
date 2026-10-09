using System.Text.Json;

namespace MintChatManager.Commands;

public sealed class OnlineMembersCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    private const int PageSize = 15;

    public string Name => "/olist";
    public string Description => "Показать список участников беседы по статусу";
    public decimal MinimumRole => 2;
    public IReadOnlyCollection<string> Aliases => new[] { "/онлайн", "/олист", "/onlinelist" };

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

        var members = await api.GetConversationMembersAsync(context.PeerId, cancellationToken);
        var actor = members.FirstOrDefault(member => member.UserId == context.UserId);
        var role = await store.GetEffectiveRoleAsync(
            context.PeerId, context.UserId, administratorIds, cancellationToken);
        if (role < 2 && actor is not { IsAdmin: true } and not { IsOwner: true })
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        var isOfflineList = context.CommandName is "/offlinelist" or "/offline" or "/офлайн";
        var page = int.TryParse(arguments.Trim(), out var requestedPage)
            ? Math.Clamp(requestedPage, 1, int.MaxValue / PageSize)
            : 1;
        var filteredMembers = members
            .Where(member => !member.IsDeactivated &&
                             (isOfflineList
                                 ? !member.IsOnline && !member.IsOnlineMobile
                                 : member.IsOnline || member.IsOnlineMobile))
            .OrderBy(member => member.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.LastName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var pageCount = Math.Max(1, (filteredMembers.Length + PageSize - 1) / PageSize);
        page = Math.Min(page, pageCount);
        var pageMembers = filteredMembers.Skip((page - 1) * PageSize).Take(PageSize).ToArray();
        var title = isOfflineList ? "Офлайн-участники" : "Онлайн-участники";
        var text = $"...::Список беседы::...\n\n| {title}: {filteredMembers.Length}\n";
        if (pageMembers.Length == 0)
        {
            text += "\nВ этом списке пока никого нет.";
        }
        else
        {
            text += "\n" + string.Join('\n', pageMembers.Select((member, index) =>
            {
                var number = (page - 1) * PageSize + index + 1;
                var status = isOfflineList
                    ? FormatLastSeen(member.LastSeenUnixTime)
                    : member.IsOnlineMobile
                        ? $"онлайн · {FormatPlatform(member.Platform)}"
                        : "онлайн";
                return $"{number}) {FormatName(member)} — {status}";
            }));
        }

        var buttons = new List<object[]>();
        if (page > 1 || page < pageCount)
        {
            var navigation = new List<object>();
            if (page > 1)
            {
                navigation.Add(CreateButton("◀", $"{context.CommandName} {page - 1}", "primary"));
            }
            if (page < pageCount)
            {
                navigation.Add(CreateButton("▶", $"{context.CommandName} {page + 1}", "primary"));
            }
            buttons.Add(navigation.ToArray());
        }

        buttons.Add(new[]
        {
            CreateButton(isOfflineList ? "Показать онлайн" : "Показать офлайн",
                isOfflineList ? "/olist 1" : "/offlinelist 1", "secondary")
        });
        var keyboard = JsonSerializer.Serialize(new
        {
            inline = true,
            buttons = buttons.Select(row => row.Select(button => button).ToArray())
        });
        if (context.IsCallback)
        {
            await context.EditMessageAsync(api, text, keyboard, cancellationToken);
        }
        else
        {
            await context.SendMessageWithKeyboardAsync(api, text, keyboard, cancellationToken);
        }
    }

    private static object CreateButton(string label, string command, string color) =>
        new
        {
            action = new
            {
                type = "callback",
                label,
                payload = new { command }
            },
            color
        };

    private static string FormatName(VkConversationMember member)
    {
        var name = $"{member.FirstName} {member.LastName}".Trim();
        return $"[id{member.UserId}|{(name.Length == 0 ? "Участник" : name)}]";
    }

    private static string FormatLastSeen(long? unixTime)
    {
        if (unixTime is null)
        {
            return "время последнего посещения неизвестно";
        }

        var localTime = DateTimeOffset.FromUnixTimeSeconds(unixTime.Value).ToOffset(TimeSpan.FromHours(3));
        return $"был(а) {localTime:dd.MM.yyyy HH:mm} МСК";
    }

    private static string FormatPlatform(int platform) => platform switch
    {
        1 => "мобильная версия",
        2 => "iPhone",
        3 => "iPad",
        4 => "Android",
        5 => "Windows Phone",
        6 => "приложение VK",
        7 => "сайт VK",
        _ => "мобильное устройство"
    };
}
