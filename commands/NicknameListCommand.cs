using System.Globalization;

namespace MintChatManager.Commands;

public sealed class NicknameListCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    private const int PageSize = 20;

    public string Name => "/nlist";
    public string Description => "Показать список Nick_Name беседы";
    public decimal MinimumRole => 1;
    public IReadOnlyCollection<string> Aliases =>
        new[] { "/никлист", "/списокников", "/списокникнеймов", "/nicks", "/ники" };

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

        var pageIsValid = int.TryParse(
            arguments.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var requestedPage) &&
                          requestedPage > 0 &&
                          requestedPage <= int.MaxValue / PageSize;
        if (!string.IsNullOrWhiteSpace(arguments) && !pageIsValid)
        {
            await context.SendMessageAsync(api, "Номер страницы должен быть положительным целым числом.", cancellationToken);
            return;
        }

        var page = pageIsValid ? requestedPage : 1;
        var offset = (page - 1) * PageSize;
        var result = await store.GetChatNicknamesAsync(context.PeerId, PageSize, offset, cancellationToken);
        var lastPage = Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)PageSize));
        if (page > lastPage)
        {
            await context.SendMessageAsync(
                api,
                $"Такой страницы нет. Последняя страница списка: {lastPage}.",
                cancellationToken);
            return;
        }

        if (result.TotalCount == 0)
        {
            await context.SendMessageAsync(api, "В этой беседе пока нет установленных Nick_Name.", cancellationToken);
            return;
        }

        var lines = result.Nicknames.Select((entry, index) =>
            $"{offset + index + 1}. [id{entry.UserId}|Участник] — {entry.Nickname}");
        var message = $"...::Список пользователей с Nick_Name::...\n\n" +
                      string.Join('\n', lines) +
                      $"\n\nСтраница {page}/{lastPage} · Всего: {result.TotalCount}\n" +
                      $"Следующая: {Name} {Math.Min(page + 1, lastPage)}";
        if (page > 1)
        {
            message += $" · Предыдущая: {Name} {page - 1}";
        }

        await context.SendMessageAsync(api, message, cancellationToken);
    }
}
