using System.Globalization;

namespace MintChatManager.Commands;

public sealed class GetNicknameCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/gnick";
    public string Description => "Показать Nick_Name участника беседы";
    public decimal MinimumRole => 1;
    public IReadOnlyCollection<string> Aliases => new[] { "/гник", "/инфоник" };

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
                api, store, context, administratorIds, requiredRole: MinimumRole, cancellationToken))
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        var (targetId, _) = CommandTargetParser.Resolve(context, arguments);
        if (targetId is null || targetId <= 0)
        {
            await context.SendMessageAsync(
                api,
                $"Укажите пользователя: {Name} [ссылка/упоминание] или ответьте на его сообщение.",
                cancellationToken);
            return;
        }

        var nickname = await store.GetChatNicknameAsync(context.PeerId, targetId.Value, cancellationToken);
        var title = $"...::Информация о Nick_Name пользователя " +
                    $"{ModerationCommandAccess.FormatMention(targetId.Value, "пользователя")}::...\n\n";
        var message = nickname is null
            ? title + "| У пользователя отсутствует Nick_Name"
            : title +
              $"| Никнейм: {nickname.Nickname}\n" +
              $"| Модератор, установивший Nick_Name: " +
              $"{ModerationCommandAccess.FormatMention(nickname.SetBy, "модератор")}\n" +
              $"| Дата установления: {nickname.UpdatedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)} UTC";
        await context.SendMessageAsync(api, message, cancellationToken);
    }
}
