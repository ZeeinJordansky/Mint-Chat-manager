using System.Globalization;

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

        var lastActivity = stats.ChatLastActivityAt?.ToOffset(TimeSpan.FromHours(3));
        var message =
            $"📊 Информация о пользователе [id{stats.UserId}|пользователе] в этой беседе\n\n" +
            $"🆔 VK ID: {stats.UserId}\n" +
            "✏️ Nick_Name: нету\n\n" +
            $"🛡️ Уровень прав: {BotRoleNames.GetDisplayName(stats.Role)}\n\n" +
            $"⚠️ Предупреждений: {stats.WarningCount}\n" +
            $"🔇 Блокировка чата: {(stats.IsMuted ? "да" : "нет")}\n\n" +
            $"💬 Сообщений в беседе (С/В): {stats.MessagesToday.ToString("N0", CultureInfo.GetCultureInfo("en-US"))}/" +
            $"{stats.ChatMessagesAllTime.ToString("N0", CultureInfo.GetCultureInfo("en-US"))}\n\n" +
            $"📅 В системе с: {stats.CreatedAt.ToOffset(TimeSpan.FromHours(3)):dd.MM.yyyy}\n" +
            $"🕐 Последняя активность: {(lastActivity is null ? "Отсутствует" : $"{lastActivity:HH:mm:ss | dd.MM.yyyy} МСК")}";

        await context.SendMessageAsync(api, message, cancellationToken);
    }
}
