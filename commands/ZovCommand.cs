using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace MintChatManager.Commands;

public sealed class ZovCommand(PostgresStore store, IReadOnlySet<long> administratorIds) : IBotCommand
{
    private static readonly ConcurrentDictionary<long, DateTimeOffset> LastStartedByPeer = new();
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(3);

    public string Name => "/zov";
    public string Description => "Подготовить вызов участников беседы";
    public decimal MinimumRole => 2;
    public IReadOnlyCollection<string> Aliases => new[] { "/зов", "/все", "/призыв", "/callall" };

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

        var members = await api.GetConversationMembersAsync(context.PeerId, cancellationToken);
        var author = members.FirstOrDefault(member => member.UserId == context.UserId);
        var role = await ModerationCommandAccess.GetEffectiveRoleAsync(
            api, store, context.PeerId, context.UserId, administratorIds, cancellationToken);
        if (role < MinimumRole && author is not { IsAdmin: true } and not { IsOwner: true })
        {
            await context.SendMessageAsync(api, "❌ Ваш уровень прав недостаточен для этой команды.", cancellationToken);
            return;
        }

        var reason = arguments.Trim();
        if (reason.Length > 250)
        {
            await context.SendMessageAsync(api, "Причина вызова не должна превышать 250 символов.", cancellationToken);
            return;
        }

        if (reason.Length == 0)
        {
            reason = "без причины";
        }

        var now = DateTimeOffset.UtcNow;
        if (!TryStartCooldown(context.PeerId, now, out var remaining))
        {
            await context.SendMessageAsync(
                api,
                $"⚠️ Повторный вызов будет доступен через {Math.Ceiling(remaining.TotalSeconds)} сек. Ограничение действует для всей беседы.",
                cancellationToken);
            return;
        }

        var encodedReason = EncodeReason(reason);
        var keyboard = JsonSerializer.Serialize(new
        {
            inline = true,
            buttons = new[]
            {
                new[]
                {
                    CreateButton("Всех", $"/zovconfirm {context.UserId} all {encodedReason}", "positive"),
                    CreateButton("Онлайн", $"/zovconfirm {context.UserId} online {encodedReason}", "positive")
                },
                new[]
                {
                    CreateButton("Не вызывать", $"/zovcancel {context.UserId}", "negative")
                }
            }
        });
        await context.SendMessageWithKeyboardAsync(
            api,
            "Вы собираетесь вызвать участников беседы. Выберите, кого вызвать:",
            keyboard,
            cancellationToken);
    }

    internal static string EncodeReason(string reason) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(reason))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    internal static bool TryStartCooldown(long peerId, DateTimeOffset now, out TimeSpan remaining)
    {
        while (true)
        {
            if (!LastStartedByPeer.TryGetValue(peerId, out var previous))
            {
                if (LastStartedByPeer.TryAdd(peerId, now))
                {
                    remaining = TimeSpan.Zero;
                    PruneCooldowns(now);
                    return true;
                }

                continue;
            }

            var elapsed = now - previous;
            if (elapsed < Cooldown)
            {
                remaining = Cooldown - elapsed;
                return false;
            }

            if (LastStartedByPeer.TryUpdate(peerId, now, previous))
            {
                remaining = TimeSpan.Zero;
                PruneCooldowns(now);
                return true;
            }
        }
    }

    private static void PruneCooldowns(DateTimeOffset now)
    {
        if (LastStartedByPeer.Count <= 512)
        {
            return;
        }

        foreach (var (peerId, timestamp) in LastStartedByPeer)
        {
            if (now - timestamp >= Cooldown)
            {
                LastStartedByPeer.TryRemove(new KeyValuePair<long, DateTimeOffset>(peerId, timestamp));
            }
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
