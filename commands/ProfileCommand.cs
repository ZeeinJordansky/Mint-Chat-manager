using System.Text.Json;

namespace MintChatManager.Commands;

public sealed class ProfileCommand(PostgresStore store) : IBotCommand
{
    public string Name => "/профиль";
    public string Description => "Показать сохранённый профиль этого чата";

    public async Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var profile = await store.GetProfileAsync(context.UserId, cancellationToken)
            ?? throw new InvalidOperationException($"No database profile exists for user {context.UserId}.");
        using var settings = JsonDocument.Parse(profile.SettingsJson);
        var settingsCount = settings.RootElement.EnumerateObject().Count();
        var message =
            $"Профиль пользователя\n" +
            $"ID пользователя: {context.UserId}\n" +
            $"Сообщений обработано: {profile.MessagesCount}\n" +
            $"Профиль создан: {profile.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}\n" +
            $"Сохранённых настроек: {settingsCount}";

        await api.SendMessageAsync(context.PeerId, message, cancellationToken);
    }
}
