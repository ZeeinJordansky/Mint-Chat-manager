namespace MintChatManager.Commands;

public sealed class IdCommand : IBotCommand
{
    public string Name => "/id";
    public string Description => "Показать ID пользователя или сообщества";
    public IReadOnlyCollection<string> Aliases => new[] { "/айди", "/ид" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var (parsedId, _) = CommandTargetParser.Resolve(context, arguments);
        var targetId = parsedId ?? context.UserId;
        var name = await api.GetSubjectNameAsync(targetId, cancellationToken) ??
                   (targetId < 0 ? "Сообщество" : $"User{targetId}");

        var text = targetId > 0
            ? $"🆔 Информация о пользователе [id{targetId}|{name}]\n\n" +
              $"🔢 VK ID: {targetId}\n" +
              "📅 Регистрация VK: Не определено"
            : $"🆔 Информация о сообществе [club{Math.Abs(targetId)}|{name}]\n\n" +
              $"🔢 VK ID: {Math.Abs(targetId)}\n" +
              $"🔗 Ссылка на сообщество: https://vk.com/club{Math.Abs(targetId)}";
        await context.SendMessageAsync(api, text, cancellationToken);
    }
}
