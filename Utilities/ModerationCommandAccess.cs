namespace MintChatManager;

internal static class ModerationCommandAccess
{
    public static bool IsConversation(long peerId) => peerId > 2_000_000_000;

    public static async Task<bool> CanModerateAsync(
        VkApiClient api,
        PostgresStore store,
        BotCommandContext context,
        IReadOnlySet<long> administratorIds,
        decimal requiredRole,
        CancellationToken cancellationToken)
    {
        if (administratorIds.Contains(context.UserId))
        {
            return true;
        }

        var role = await GetEffectiveRoleAsync(
            api, store, context.PeerId, context.UserId, administratorIds, cancellationToken);
        return role >= requiredRole;
    }

    public static async Task<bool> CanTargetAsync(
        VkApiClient api,
        PostgresStore store,
        BotCommandContext context,
        IReadOnlySet<long> administratorIds,
        long targetId,
        CancellationToken cancellationToken)
    {
        if (administratorIds.Contains(context.UserId))
        {
            return true;
        }
        if (targetId == context.UserId)
        {
            return false;
        }

        var ownerId = IsConversation(context.PeerId)
            ? await api.GetConversationOwnerIdAsync(context.PeerId, cancellationToken)
            : null;
        var actorRole = administratorIds.Contains(context.UserId)
            ? 12m
            : await store.GetEffectiveRoleAsync(context.PeerId, context.UserId, administratorIds, cancellationToken);
        var targetRole = administratorIds.Contains(targetId)
            ? 12m
            : await store.GetEffectiveRoleAsync(context.PeerId, targetId, administratorIds, cancellationToken);
        if (ownerId == context.UserId)
        {
            actorRole = Math.Max(actorRole, 7);
        }
        if (ownerId == targetId)
        {
            targetRole = Math.Max(targetRole, 7);
        }

        return targetRole < actorRole;
    }

    public static async Task<decimal> GetEffectiveRoleAsync(
        VkApiClient api,
        PostgresStore store,
        long peerId,
        long userId,
        IReadOnlySet<long> administratorIds,
        CancellationToken cancellationToken)
    {
        var role = await store.GetEffectiveRoleAsync(peerId, userId, administratorIds, cancellationToken);
        if (IsConversation(peerId) &&
            await api.GetConversationOwnerIdAsync(peerId, cancellationToken) == userId)
        {
            return Math.Max(role, 7);
        }

        return role;
    }

    public static string FormatMention(long userId, string label) => $"[id{userId}|{label}]";
}
