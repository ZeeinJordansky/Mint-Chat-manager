using System.Text.Json;

namespace MintChatManager.Commands;

public sealed class SettingsCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    public string Name => "/settings";
    public string Description => "Показать и изменить настройки чат-менеджера";
    public decimal MinimumRole => 6;
    public IReadOnlyCollection<string> Aliases => new[] { "/настройки", "/настройка", "/сеттингс", "/параметры" };

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
        if (role < MinimumRole)
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        var settings = await store.GetChatSettingsAsync(context.PeerId, cancellationToken);
        var parts = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && parts[0].ToLowerInvariant() is "prefix" or "префикс")
        {
            var value = parts.Length > 1 ? parts[1].ToLowerInvariant() : "toggle";
            if (value is "toggle" or "переключить")
            {
                settings = settings with { NoPrefix = !settings.NoPrefix };
            }
            else if (value is "on" or "вкл" or "включить")
            {
                settings = settings with { NoPrefix = true };
            }
            else if (value is "off" or "выкл" or "выключить")
            {
                settings = settings with { NoPrefix = false };
            }
            else
            {
                await context.SendMessageAsync(
                    api, "Используйте /settings prefix on, /settings prefix off или кнопку переключения.", cancellationToken);
                return;
            }

            await store.UpdateChatSettingsAsync(
                context.PeerId, "no_prefix", settings.NoPrefix.ToString(), cancellationToken);
        }
        else if (parts.Length > 0 && parts[0].ToLowerInvariant() is "active" or "бот")
        {
            var value = parts.Length > 1 ? parts[1].ToLowerInvariant() : "toggle";
            if (value is "toggle" or "переключить")
            {
                settings = settings with { IsActive = !settings.IsActive };
            }
            else if (value is "on" or "вкл" or "включить")
            {
                settings = settings with { IsActive = true };
            }
            else if (value is "off" or "выкл" or "выключить")
            {
                settings = settings with { IsActive = false };
            }
            else
            {
                await context.SendMessageAsync(
                    api, "Используйте /settings active on, /settings active off или кнопку переключения.", cancellationToken);
                return;
            }

            await store.UpdateChatSettingsAsync(
                context.PeerId, "active", settings.IsActive.ToString(), cancellationToken);
        }

        settings = await store.GetChatSettingsAsync(context.PeerId, cancellationToken);
        var text =
            "⚙️ Настройки чат-менеджера\n\n" +
            $"Статус: {(settings.IsActive ? "включён" : "выключен")}\n" +
            $"Команды без префикса: {(settings.NoPrefix ? "включены" : "выключены")}\n\n" +
            "Выберите настройку кнопкой или используйте /settings prefix|active on|off.";
        var keyboard = JsonSerializer.Serialize(new
        {
            inline = true,
            buttons = new[]
            {
                new[]
                {
                    CreateButton(
                        settings.NoPrefix ? "Выключить команды без префикса" : "Включить команды без префикса",
                        "/settings prefix toggle",
                        "secondary")
                },
                new[]
                {
                    CreateButton(
                        settings.IsActive ? "Выключить чат-менеджер" : "Включить чат-менеджер",
                        "/settings active toggle",
                        settings.IsActive ? "negative" : "positive")
                }
            }
        });
        if (context.IsCallback)
        {
            await context.EditMessageAsync(api, text, keyboard, cancellationToken);
        }
        else
        {
            await context.SendMessageWithKeyboardAsync(api, text, keyboard, cancellationToken);
        }
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
}
