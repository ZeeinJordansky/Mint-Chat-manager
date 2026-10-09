namespace MintChatManager;

public sealed class BotStatistics
{
    private readonly DateTimeOffset startedAt = DateTimeOffset.UtcNow;
    private long processedMessages;
    private long executedCommands;

    public TimeSpan Uptime => DateTimeOffset.UtcNow - startedAt;
    public long ProcessedMessages => Interlocked.Read(ref processedMessages);
    public long ExecutedCommands => Interlocked.Read(ref executedCommands);

    public void RecordMessage() => Interlocked.Increment(ref processedMessages);
    public void RecordCommand() => Interlocked.Increment(ref executedCommands);
}
