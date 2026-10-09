using System.Globalization;

namespace MintChatManager.Commands;

public sealed class ProfileCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/stats";
    public string Description => "Показать статистику пользователя";
    public IReadOnlyCollection<string> Aliases =>
        new[] { "/стата", "/статистика", "/статс", "/stata", "/профиль", "/profile", "/я" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var (targetId, _) = CommandTargetParser.Resolve(context, arguments);
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

        var moscowTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(stats.LastSeenAt, "Europe/Moscow");
        var message =
            "┌─ 📊 Статистика пользователя\n" +
            $"│ Пользователь: [id{stats.UserId}|профиль]\n" +
            $"│ Должность: {BotRoleNames.GetDisplayName(stats.Role)}\n" +
            $"│ Предупреждений: {stats.WarningCount}\n" +
            $"│ Активная блокировка чата: {(stats.IsMuted ? "да" : "нет")}\n" +
            $"│ Сообщений сегодня: {stats.MessagesToday.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))}\n" +
            $"│ Сообщений за неделю: {stats.MessagesThisWeek.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))}\n" +
            $"│ Сообщений всего: {stats.MessagesAllTime.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))}\n" +
            $"│ Последняя активность: {moscowTime:dd.MM.yyyy HH:mm:ss} МСК\n" +
            "└────────────────────────";

        await context.SendMessageAsync(api, message, cancellationToken);
    }
}
