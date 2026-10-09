using System.Globalization;
using System.Text.Json;

namespace MintChatManager.Commands;

public sealed class TopCommand(PostgresStore store) : IBotCommand
{
    private const int ResultLimit = 10;
    private static readonly (string Key, string Title)[] Categories =
    [
        ("messages", "Сообщения"),
        ("money", "Деньги"),
        ("bank", "Банк"),
        ("beer", "Пиво"),
        ("btc", "Bitcoin"),
        ("reputation", "Репутация")
    ];

    public string Name => "/top";
    public string Description => "Открыть игровой топ";
    public IReadOnlyCollection<string> Aliases => new[] { "/топ", "/топы", "/пивозавры" };

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var categoryKey = arguments.Trim().ToLowerInvariant();
        if (categoryKey.Length == 0)
        {
            await SendPageAsync(
                api,
                context,
                "🏆 Выберите категорию топа:",
                CreateCategoryKeyboard(),
                cancellationToken);
            return;
        }

        var category = Categories.FirstOrDefault(item => item.Key == categoryKey);
        if (category == default)
        {
            await context.SendMessageAsync(
                api,
                $"Неизвестная категория. Используйте: {string.Join(", ", Categories.Select(item => item.Key))}.",
                cancellationToken);
            return;
        }

        var entries = await store.GetGameLeaderboardAsync(
            context.PeerId, category.Key, ResultLimit, cancellationToken);
        if (entries.Count == 0)
        {
            await SendPageAsync(
                api,
                context,
                $"🏆 Топ «{category.Title}»\n\nВ этой категории пока нет данных.",
                CreateCategoryKeyboard(),
                cancellationToken);
            return;
        }

        var names = await Task.WhenAll(entries.Select(async entry =>
            (entry.UserId, entry.Score, Name: await api.GetSubjectNameAsync(entry.UserId, cancellationToken))));
        var lines = names.Select((entry, index) =>
            $"{index + 1}. [id{entry.UserId}|{entry.Name ?? "Пользователь"}] — " +
            $"{entry.Score.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))}");
        var text = $"🏆 Топ «{category.Title}»\n\n{string.Join('\n', lines)}";
        await SendPageAsync(api, context, text, CreateCategoryKeyboard(category.Key), cancellationToken);
    }

    private static Task SendPageAsync(
        VkApiClient api,
        BotCommandContext context,
        string text,
        string keyboard,
        CancellationToken cancellationToken)
    {
        return context.IsCallback
            ? context.EditMessageAsync(api, text, keyboard, cancellationToken)
            : context.SendMessageWithKeyboardAsync(
                api, text, keyboard, cancellationToken, quoteCommand: false, disableMentions: true);
    }

    private static string CreateCategoryKeyboard(string? selected = null)
    {
        var buttons = Categories
            .Where(category => category.Key != selected)
            .Chunk(2)
            .Select(row => row.Select(category =>
                new
                {
                    action = new
                    {
                        type = "callback",
                        label = category.Title,
                        payload = new { command = $"/top {category.Key}" }
                    },
                    color = "secondary"
                }).ToArray())
            .Append(new[]
            {
                new
                {
                    action = new
                    {
                        type = "callback",
                        label = "Категории",
                        payload = new { command = "/top" }
                    },
                    color = "primary"
                }
            });
        return JsonSerializer.Serialize(new { inline = true, buttons });
    }
}
