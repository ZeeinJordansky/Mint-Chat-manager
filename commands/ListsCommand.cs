using System.Globalization;
using System.Text.Json;

namespace MintChatManager.Commands;

public sealed class ListsCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    private const int PageSize = 15;
    private static readonly CultureInfo DateCulture = CultureInfo.InvariantCulture;

    public string Name => "/lists";
    public string Description => "Открыть списки наказаний беседы";
    public IReadOnlyCollection<string> Aliases =>
        new[] { "/листы", "/списки", "/slists", "/list", "/спис", "/список", "/наказания" };

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

        var role = await ModerationCommandAccess.GetEffectiveRoleAsync(
            api, store, context.PeerId, context.UserId, administratorIds, cancellationToken);
        if (role < 1)
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        var parts = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var category = parts.Length > 0 ? parts[0].ToLowerInvariant() : "main";
        var page = parts.Length > 1 && long.TryParse(parts[1], out var requestedPage)
            ? (int)Math.Clamp(requestedPage, 1, int.MaxValue / PageSize)
            : 1;
        if (category is not ("main" or "warns" or "mutes" or "bans") ||
            category == "bans" && role <= 2)
        {
            await context.SendMessageAsync(api, "Неизвестная или недоступная категория списка.", cancellationToken);
            return;
        }

        if (category == "main")
        {
            var mainKeyboard = CreateKeyboard(new[]
            {
                new[] { CreateButton("С предупреждениями", "/lists warns 1", "secondary") },
                new[] { CreateButton("С блокировкой чата", "/lists mutes 1", "secondary") }
            }.Concat(role > 2
                ? new[] { new[] { CreateButton("С блокировкой", "/lists bans 1", "secondary") } }
                : Array.Empty<Button[]>()).ToArray());
            await SendListAsync(
                api,
                context,
                "...::Список пользователей с наказаниями::...\n\nУкажите категорию:",
                mainKeyboard,
                cancellationToken);
            return;
        }

        var actionType = category switch
        {
            "warns" => "warn",
            "mutes" => "mute",
            "bans" => "ban",
            _ => throw new InvalidOperationException($"Unsupported moderation list category '{category}'.")
        };
        var records = await store.GetModerationListAsync(
            context.PeerId,
            actionType,
            cancellationToken,
            PageSize + 1,
            (page - 1) * PageSize);
        var hasNextPage = records.Count > PageSize;
        if (hasNextPage)
        {
            records = records.Take(PageSize).ToArray();
        }

        var heading = category switch
        {
            "warns" => "С предупреждениями",
            "mutes" => "С блокировкой чата",
            "bans" => "С блокировкой",
            _ => throw new InvalidOperationException()
        };
        if (records.Count == 0 && page > 1)
        {
            page--;
            records = await store.GetModerationListAsync(
                context.PeerId,
                actionType,
                cancellationToken,
                PageSize + 1,
                (page - 1) * PageSize);
            hasNextPage = records.Count > PageSize;
            if (hasNextPage)
            {
                records = records.Take(PageSize).ToArray();
            }
        }

        var text = $"...::Список пользователей с наказаниями::...\n\n| Категория: {heading}\n\n";
        if (records.Count == 0)
        {
            text += category switch
            {
                "warns" => "В этой беседе нет пользователей с предупреждениями.",
                "mutes" => "В этой беседе нет пользователей с активной блокировкой чата.",
                "bans" => "В этой беседе нет заблокированных пользователей.",
                _ => ""
            };
        }
        else
        {
            var firstIndex = (page - 1) * PageSize;
            text += string.Join(
                '\n',
                records.Select((record, index) =>
                {
                    var remaining = record.ExpiresAt is null
                        ? "Навсегда"
                        : $"до {record.ExpiresAt.Value.ToString("dd.MM.yyyy HH:mm", DateCulture)} UTC";
                    var extra = category switch
                    {
                        "warns" => $" | Причина: {record.Reason} | Дата: {record.CreatedAt.ToString("dd.MM.yyyy HH:mm", DateCulture)} UTC",
                        "mutes" => $" | Срок: {remaining} | Причина: {record.Reason}",
                        _ => $" | Срок: {remaining} | Причина: {record.Reason}"
                    };
                    return $"{firstIndex + index + 1}) {ModerationCommandAccess.FormatMention(record.TargetId, "Пользователь")} " +
                           $"| Модератор - {ModerationCommandAccess.FormatMention(record.ActorId, "модератор")}{extra}";
                }));
        }

        var buttons = new List<Button[]>();
        if (page > 1 || hasNextPage)
        {
            var nav = new List<Button>();
            if (page > 1)
            {
                nav.Add(CreateButton("◀", $"/lists {category} {page - 1}", "primary"));
            }
            if (hasNextPage)
            {
                nav.Add(CreateButton("▶", $"/lists {category} {page + 1}", "primary"));
            }
            buttons.Add(nav.ToArray());
        }
        buttons.Add(new[] { CreateButton("Назад", "/lists main", "negative") });
        var keyboard = CreateKeyboard(buttons.ToArray());
        await SendListAsync(
            api,
            context,
            text,
            keyboard,
            cancellationToken);
    }

    private static Task SendListAsync(
        VkApiClient api,
        BotCommandContext context,
        string text,
        string keyboard,
        CancellationToken cancellationToken) =>
        context.IsCallback
            ? context.EditMessageAsync(api, text, keyboard, cancellationToken)
            : context.SendMessageWithKeyboardAsync(api, text, keyboard, cancellationToken);

    private static Button CreateButton(string label, string command, string color) =>
        new(label, command, color);

    private static string CreateKeyboard(Button[][] buttons) =>
        JsonSerializer.Serialize(new
        {
            inline = true,
            buttons = buttons.Select(row => row.Select(button => new
            {
                action = new
                {
                    type = "callback",
                    label = button.Label,
                    payload = new { command = button.Command }
                },
                color = button.Color
            }))
        });

    private sealed record Button(string Label, string Command, string Color);
}
