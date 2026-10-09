using System.Globalization;
using System.Text.Json;

namespace MintChatManager.Commands;

public sealed class ProfileCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/stats";
    public string Description => "Показать статистику пользователя";
    public IReadOnlyCollection<string> Aliases =>
        new[] { "/стата", "/статистика", "/статс", "/stata", "/профиль", "/profile", "/я", "/си", "/систата" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var parts = arguments.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        var page = parts.Length > 0 && (parts[0] is "activity" or "game")
            ? parts[0]
            : "main";
        var targetArguments = page == "main" ? arguments : parts.Length > 1 ? parts[1] : "";
        var (targetId, _) = CommandTargetParser.Resolve(context, targetArguments);
        targetId ??= context.UserId;
        var stats = await store.GetUserStatisticsAsync(
            context.PeerId,
            targetId.Value,
            administratorIds,
            cancellationToken);
        if (stats is null)
        {
            await context.SendMessageAsync(
                api,
                $"Для [id{targetId.Value}|пользователя] пока нет сохранённой статистики.",
                cancellationToken);
            return;
        }

        var actorRole = await store.GetEffectiveRoleAsync(
            context.PeerId, context.UserId, administratorIds, cancellationToken);
        var mayViewActivity = actorRole >= 1 || administratorIds.Contains(context.UserId);
        if (page == "activity" && !mayViewActivity)
        {
            await context.SendMessageAsync(
                api, "❌ Для просмотра активности требуется уровень прав от 1.", cancellationToken);
            return;
        }

        if (page == "game")
        {
            var account = await store.GetGameAccountAsync(targetId.Value, cancellationToken);
            var gameText =
                $"🎮 Игровой профиль [id{targetId.Value}|пользователя]\n\n" +
                $"💰 На руках: {account.WalletBalance.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))}$\n" +
                $"🏦 В банке: {account.BankBalance.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))}$\n" +
                $"⭐ Репутация: {account.Reputation.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))}\n" +
                $"🍺 Пиво: {account.Beer.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))}\n" +
                $"₿ Bitcoin: {account.Bitcoin.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))}";
            await SendStatsPageAsync(
                api, context, gameText, targetId.Value, "game", mayViewActivity, cancellationToken);
            return;
        }

        if (page == "activity")
        {
            var activityText =
                $"...::Информация об активности [id{targetId.Value}|пользователя] в этой беседе::...\n\n" +
                $"| Сообщений за сегодня: {stats.MessagesToday.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))}\n" +
                $"| Сообщений за неделю: {stats.MessagesThisWeek.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))}\n" +
                $"| Сообщений за всё время: {stats.ChatMessagesAllTime.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))}";
            await SendStatsPageAsync(
                api, context, activityText, targetId.Value, "activity", mayViewActivity, cancellationToken);
            return;
        }

        var nickname = await store.GetChatNicknameAsync(context.PeerId, targetId.Value, cancellationToken);
        var targetName = await api.GetSubjectNameAsync(targetId.Value, cancellationToken) ?? "пользователя";
        var chatRank = await store.GetChatMessageRankAsync(context.PeerId, targetId.Value, cancellationToken);
        var lastActivity = stats.ChatLastActivityAt?.ToOffset(TimeSpan.FromHours(3));
        var message =
            $"📊 Информация о пользователе [id{stats.UserId}|{targetName}] в этой беседе\n\n" +
            $"🆔 VK ID: {stats.UserId}\n" +
            $"✏️ Nick_Name: {nickname?.Nickname ?? "нету"}\n\n" +
            $"🛡️ Уровень прав: {BotRoleNames.GetDisplayName(stats.Role)}\n\n" +
            $"⚠️ Предупреждений: {stats.WarningCount}\n" +
            $"🔇 Блокировка чата: {(stats.IsMuted ? "да" : "нет")}\n\n" +
            $"💬 Сообщений в беседе (С/В): {stats.MessagesToday}/{stats.ChatMessagesAllTime} | Топ {chatRank}\n\n" +
            $"📅 В системе с: {stats.CreatedAt.ToOffset(TimeSpan.FromHours(3)):dd.MM.yyyy}\n" +
            $"🕐 Последняя активность: {(lastActivity is null ? "Отсутствует" : $"{lastActivity:HH:mm:ss | dd.MM.yyyy} МСК")}";

        await SendStatsPageAsync(api, context, message, targetId.Value, "main", mayViewActivity, cancellationToken);
    }

    private static Task SendStatsPageAsync(
        VkApiClient api,
        BotCommandContext context,
        string text,
        long targetId,
        string page,
        bool mayViewActivity,
        CancellationToken cancellationToken)
    {
        var buttons = new List<object[]>();
        if (page == "main")
        {
            if (mayViewActivity)
            {
                buttons.Add(new[] { CreateButton("Активность", $"/stats activity {targetId}") });
            }
            buttons.Add(new[] { CreateButton("Игровой профиль", $"/stats game {targetId}") });
        }
        else
        {
            buttons.Add(new[] { CreateButton("Общая информация", $"/stats {targetId}") });
            if (page == "game" && mayViewActivity)
            {
                buttons.Add(new[] { CreateButton("Активность", $"/stats activity {targetId}") });
            }
            else if (page == "activity")
            {
                buttons.Add(new[] { CreateButton("Игровой профиль", $"/stats game {targetId}") });
            }
        }

        var keyboard = JsonSerializer.Serialize(new { inline = true, buttons });
        return context.IsCallback
            ? context.EditMessageAsync(api, text, keyboard, cancellationToken)
            : context.SendMessageWithKeyboardAsync(
                api, text, keyboard, cancellationToken, disableMentions: true);
    }

    private static object CreateButton(string label, string command) => new
    {
        action = new
        {
            type = "callback",
            label,
            payload = new { command }
        },
        color = "secondary"
    };
}
