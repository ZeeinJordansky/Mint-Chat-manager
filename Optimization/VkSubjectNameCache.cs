using System.Collections.Concurrent;

namespace MintChatManager.Optimization;

internal sealed class VkSubjectNameCache
{
    private const int MaximumEntries = 2_048;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<long, CacheEntry> entries = new();

    public bool TryGet(long subjectId, out string name)
    {
        if (entries.TryGetValue(subjectId, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
            {
                name = entry.Name;
                return true;
            }

            entries.TryRemove(new KeyValuePair<long, CacheEntry>(subjectId, entry));
        }

        name = "";
        return false;
    }

    public void Set(long subjectId, string name)
    {
        if (entries.Count >= MaximumEntries)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var (key, value) in entries)
            {
                if (value.ExpiresAt <= now)
                {
                    entries.TryRemove(new KeyValuePair<long, CacheEntry>(key, value));
                }
            }

            if (entries.Count >= MaximumEntries && !entries.ContainsKey(subjectId))
            {
                return;
            }
        }

        entries[subjectId] = new CacheEntry(name, DateTimeOffset.UtcNow.Add(Lifetime));
    }

    private sealed record CacheEntry(string Name, DateTimeOffset ExpiresAt);
}
