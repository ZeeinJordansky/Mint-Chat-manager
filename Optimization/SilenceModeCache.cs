using System.Collections.Concurrent;

namespace MintChatManager.Optimization;

public sealed class SilenceModeCache
{
    private readonly ConcurrentDictionary<long, CacheEntry> entries = new();

    public async Task<(bool Enabled, decimal MinimumRole)> GetAsync(
        PostgresStore store,
        long peerId,
        CancellationToken cancellationToken)
    {
        if (entries.TryGetValue(peerId, out var cached))
        {
            if (cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return cached.Settings;
            }

            entries.TryRemove(new KeyValuePair<long, CacheEntry>(peerId, cached));
        }

        var settings = await store.GetChatSilenceAsync(peerId, cancellationToken);
        if (entries.Count >= BotRuntimeTuning.MaximumCachedSilenceSettings)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var (key, value) in entries)
            {
                if (value.ExpiresAt <= now)
                {
                    entries.TryRemove(new KeyValuePair<long, CacheEntry>(key, value));
                }
            }

            if (entries.Count >= BotRuntimeTuning.MaximumCachedSilenceSettings &&
                !entries.ContainsKey(peerId))
            {
                return settings;
            }
        }

        entries[peerId] = new CacheEntry(
            settings,
            DateTimeOffset.UtcNow + BotRuntimeTuning.SilenceSettingsCacheLifetime);
        return settings;
    }

    public void Invalidate(long peerId) => entries.TryRemove(peerId, out _);

    private sealed record CacheEntry(
        (bool Enabled, decimal MinimumRole) Settings,
        DateTimeOffset ExpiresAt);
}
