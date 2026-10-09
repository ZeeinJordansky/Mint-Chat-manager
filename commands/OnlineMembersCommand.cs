using System.Text.Json;

namespace MintChatManager.Commands;

public sealed class OnlineMembersCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    private const int PageSize = 15;

    public string Name => "/olist";
    public string Description => "Показать список участников беседы по статусу";
    public decimal MinimumRole => 2;
    public IReadOnlyCollection<string> Aliases =>
        new[] { "/онлайн", "/олист", "/онлайнлист", "/online", "/onlinelist" };

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

        var isOfflineList = context.CommandName is
            "/offlinelist" or "/offline" or "/офлайн" or "/оффлайнлист" or "/офлайнлист";
        var page = int.TryParse(arguments.Trim(), out var requestedPage)
            ? Math.Clamp(requestedPage, 1, int.MaxValue / PageSize)
            : 1;
        var filteredMembers = members
            .Where(member => !member.IsDeactivated &&
                             (isOfflineList
                                 ? !member.IsOnline && !member.IsOnlineMobile && member.LastSeenUnixTime is > 0
                                 : member.IsOnline || member.IsOnlineMobile))
            .OrderBy(member => member.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.LastName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var pageCount = Math.Max(1, (filteredMembers.Length + PageSize - 1) / PageSize);
        page = Math.Min(page, pageCount);
        var pageMembers = filteredMembers.Skip((page - 1) * PageSize).Take(PageSize).ToArray();
        var title = isOfflineList ? "Пользователи оффлайн" : "Пользователи онлайн";
        var state = isOfflineList ? "оффлайн" : "онлайн";
        var text = $"{(isOfflineList ? "⚪" : "🟢")} {title}\n\nВсего {state}: {filteredMembers.Length}\n\n";
        if (pageMembers.Length == 0)
        {
            text += isOfflineList
                ? "Все участники сейчас в сети."
                : "В данный момент нет участников онлайн.";
        }
        else
        {
            text += string.Join("\n\n", pageMembers.Select(member =>
            {
                var status = isOfflineList
                    ? $"был(-а) в сети {FormatLastSeen(member.LastSeenUnixTime)}"
                    : FormatOnlinePlatform(member);
                return $"{FormatName(member)} — {status}";
            }));
        }

        var buttons = new List<object[]>();
        if (page > 1 || page < pageCount)
        {
            var previousPage = page > 1 ? page - 1 : pageCount;
            var nextPage = page < pageCount ? page + 1 : 1;
            buttons.Add(new[]
            {
                CreateButton("◀ Назад", $"{context.CommandName} {previousPage}", "primary"),
                CreateButton("Далее ▶", $"{context.CommandName} {nextPage}", "primary")
            });
        }

        buttons.Add(new[]
        {
            CreateButton(isOfflineList ? "Онлайн" : "Оффлайн",
                isOfflineList ? "/olist 1" : "/offlinelist 1",
                isOfflineList ? "primary" : "secondary")
        });
        var keyboard = JsonSerializer.Serialize(new
        {
            inline = true,
            buttons = buttons.Select(row => row.Select(button => button).ToArray())
        });
        if (context.IsCallback)
            await context.EditMessageAsync(api, text, keyboard, cancellationToken);
        else
            await context.SendMessageWithKeyboardAsync(api, text, keyboard, cancellationToken, quoteCommand: false);
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
            return "неизвестно";
        }

        var localTime = DateTimeOffset.FromUnixTimeSeconds(unixTime.Value).ToOffset(TimeSpan.FromHours(3));
        var weekdays = new[] { "воскресенье", "понедельник", "вторник", "среда", "четверг", "пятница", "суббота" };
        var months = new[]
        {
            "января", "февраля", "марта", "апреля", "мая", "июня",
            "июля", "августа", "сентября", "октября", "ноября", "декабря"
        };
        return $"{localTime:HH} ч. {localTime:mm} м. {localTime:ss} с. (МСК) | " +
               $"{weekdays[(int)localTime.DayOfWeek]}, {localTime:dd} {months[localTime.Month - 1]}, {localTime:yyyy} года";
    }

    private static string FormatOnlinePlatform(VkConversationMember member)
    {
        if (member.IsOnlineMobile)
        {
            return member.Platform is 2 or 3 ? "Iphone" : "Android";
        }

        return member.Platform is 2 or 3
            ? "Iphone"
            : member.Platform == 4
                ? "Android"
                : "PC";
    }
}
