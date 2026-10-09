using System.Text.Json;

namespace MintChatManager.Commands;

public sealed class HelpCommand(IReadOnlyCollection<IBotCommand> commands) : IBotCommand
{
    private static readonly HelpCategory[] Categories =
    [
        new("user", "Пользователь", 0),
        new("moderator", "Модератор", 1),
        new("senior", "Старший Модератор", 2),
        new("admin", "Администратор", 3),
        new("chiefadmin", "Главный Администратор", 4),
        new("leader", "Руководитель беседы", 5),
        new("owner", "Владелец беседы", 6),
        new("games", "Игровые команды", null)
    ];

    private static readonly HashSet<string> GameCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "/баланс", "/btc", "/купитьbtc", "/продатьbtc", "/банк", "/снятьбанк",
        "/передать", "/ежедневный"
    };

    public string Name => "/help";
    public string Description => "Показать список команд";
    public IReadOnlyCollection<string> Aliases =>
        new[] { "/помощь", "/хелп", "/команды", "/меню", "/cmd", "/cmds" };

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var page = arguments.Trim().ToLowerInvariant();
        var selected = Categories.FirstOrDefault(category => category.Key == page);
        var message = selected is null ? RenderMainPage() : RenderCategory(selected);
        var keyboard = CreateKeyboard(selected is null ? MainButtons() : CategoryButtons(selected));
        return context.IsCallback && context.MessageId is > 0
            ? context.EditMessageAsync(api, message, keyboard, cancellationToken)
            : context.SendMessageWithKeyboardAsync(api, message, keyboard, cancellationToken);
    }

    private string RenderCategory(HelpCategory category)
    {
        var visibleCommands = commands
            .Where(command => command is not IInternalBotCommand)
            .Where(command => category.Key == "games"
                ? GameCommands.Contains(command.Name)
                : !GameCommands.Contains(command.Name) &&
                  category.MinimumRole is { } minimumRole &&
                  command.MinimumRole <= minimumRole)
            .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var commandText = visibleCommands.Length == 0
            ? "Команды этой категории ещё не перенесены."
            : string.Join('\n', visibleCommands.Select(command =>
                $"• {command.Name} — {command.Description}"));
        return $"📖 Помощь по командам чат-менеджера\n\n" +
               $"📂 Категория: {category.Title}\n\n" +
               $"📋 Команды:\n{commandText}";
    }

    private static string RenderMainPage() =>
        "📖 Помощь по командам чат-менеджера\n\n" +
        "🔹 Префиксы команд: / ! . ; : ,\n\n" +
        "🔹 Выберите уровень прав, чтобы посмотреть доступные ему команды.";

    private static string CreateKeyboard(string[][] rows) =>
        JsonSerializer.Serialize(new
        {
            inline = true,
            buttons = rows.Select(row => row.Select(label =>
            {
                var category = Categories.FirstOrDefault(item => item.Title == label);
                var payload = label == "Главное меню"
                    ? "/help"
                    : category is null
                        ? throw new InvalidOperationException($"Unknown help category '{label}'.")
                        : $"/help {category.Key}";
                return new
                {
                    action = new
                    {
                        type = "callback",
                        label,
                        payload = new { command = payload }
                    },
                    color = label == "Игровые команды" ? "positive" : "secondary"
                };
            }))
        });

    private static string[][] MainButtons() =>
    [
        ["Пользователь"],
        ["Модератор", "Старший Модератор"],
        ["Администратор", "Главный Администратор"],
        ["Руководитель беседы", "Владелец беседы"],
        ["Игровые команды"]
    ];

    private string[][] CategoryButtons(HelpCategory selected) =>
        Categories.Where(category => category != selected)
            .Select(category => new[] { category.Title })
            .Append(new[] { "Главное меню" })
            .ToArray();

    private sealed record HelpCategory(string Key, string Title, decimal? MinimumRole);
}
