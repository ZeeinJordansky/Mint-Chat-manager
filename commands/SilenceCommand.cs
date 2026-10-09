using MintChatManager.Optimization;

namespace MintChatManager.Commands;

public sealed class SilenceCommand(
    PostgresStore store,
    IReadOnlySet<long> administratorIds,
    SilenceModeCache silenceModeCache) : IBotCommand
{
    public string Name => "/silence";
    public string Description => "Включить или выключить режим тишины в беседе";
    public decimal MinimumRole => 3;
    public IReadOnlyCollection<string> Aliases => new[] { "/тишина", "/мутчат", "/silent", "/режимтишины" };

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

        var current = await silenceModeCache.GetAsync(store, context.PeerId, cancellationToken);
        var action = arguments.Trim().ToLowerInvariant();
        var enabled = action switch
        {
            "on" or "вкл" => true,
            "off" or "выкл" => false,
            "" or "toggle" or "переключить" => !current.Enabled,
            _ => (bool?)null
        };
        if (enabled is null)
        {
            await context.SendMessageAsync(
                api, $"Использование: {Name} [on|off]", cancellationToken);
            return;
        }

        await store.UpdateChatSettingsAsync(context.PeerId, "silence", enabled.Value.ToString(), cancellationToken);
        silenceModeCache.Invalidate(context.PeerId);
        var buttonLabel = enabled.Value ? "Выключить режим тишины" : "Включить режим тишины";
        var buttonAction = enabled.Value ? "off" : "on";
        var keyboard = GameText.CreateCallbackKeyboard(
            buttonLabel,
            $"{Name} {buttonAction}",
            enabled.Value ? "negative" : "positive");
        var status = enabled.Value ? "включён" : "выключен";
        var response =
            $"Режим тишины в беседе был {status} " +
            $"{ModerationCommandAccess.FormatMention(context.UserId, "модератором")}.";

        if (context.IsCallback)
        {
            await context.EditMessageAsync(api, response, keyboard, cancellationToken);
        }
        else
        {
            await context.SendMessageWithKeyboardAsync(
                api, response, keyboard, cancellationToken, quoteCommand: false);
        }
    }
}
