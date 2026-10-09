namespace MintChatManager.Optimization;

public static class BotRuntimeTuning
{
    public const int UpdateQueueCapacity = 64;
    public const int UpdateWorkerCount = 4;
    public const int HttpMaxConnectionsPerServer = 8;
    public const int PostgreSqlMaxPoolSize = 8;
    public const int MaximumCachedSilenceSettings = 1_024;
    public static readonly TimeSpan SilenceSettingsCacheLifetime = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan HttpConnectionLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(40);
}
