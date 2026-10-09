using System.Text.Json;
using System.Threading.Channels;

namespace MintChatManager;

public sealed class LongPollBot(
    VkApiClient api,
    CommandRouter commandRouter,
    long groupId,
    BotStatistics statistics,
    PostgresStore store)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var updateChannel = Channel.CreateBounded<BotMessageUpdate>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false
        });
        const int workerCount = 4;
        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => ProcessUpdatesAsync(updateChannel.Reader, cancellationToken))
            .ToArray();

        try
        {
            await RunLongPollingAsync(updateChannel.Writer, cancellationToken);
        }
        finally
        {
            updateChannel.Writer.TryComplete();
        }

        await Task.WhenAll(workers);
    }

    private async Task RunLongPollingAsync(
        ChannelWriter<BotMessageUpdate> updateWriter,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var serverInfo = await api.GetLongPollServerAsync(groupId, cancellationToken);
                var server = serverInfo.GetProperty("server").GetString()
                    ?? throw new InvalidOperationException("VK did not return a Long Poll server.");
                var key = serverInfo.GetProperty("key").GetString()
                    ?? throw new InvalidOperationException("VK did not return a Long Poll key.");
                var timestamp = serverInfo.GetProperty("ts").GetString()
                    ?? throw new InvalidOperationException("VK did not return a Long Poll timestamp.");
                Console.WriteLine($"Connected to VK Long Poll for group {groupId}.");

                while (!cancellationToken.IsCancellationRequested)
                {
                    var pollUri = new Uri(
                        $"{server}?act=a_check&key={Uri.EscapeDataString(key)}" +
                        $"&ts={Uri.EscapeDataString(timestamp)}&wait=25");
                    using var response = await api.GetLongPollResponseAsync(pollUri, cancellationToken);
                    var result = response.RootElement;

                    if (result.TryGetProperty("failed", out _))
                    {
                        break;
                    }

                    var nextTimestamp = result.GetProperty("ts").GetString() ?? timestamp;
                    if (!result.TryGetProperty("updates", out var updates))
                    {
                        timestamp = nextTimestamp;
                        continue;
                    }

                    foreach (var update in updates.EnumerateArray())
                    {
                        var message = ParseMessageUpdate(update);
                        if (message is { } parsedMessage)
                        {
                            await updateWriter.WriteAsync(parsedMessage, cancellationToken);
                        }
                    }

                    timestamp = nextTimestamp;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Long Poll error: {exception.Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task ProcessUpdatesAsync(
        ChannelReader<BotMessageUpdate> updateReader,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in updateReader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await store.RecordMessageAsync(update.UserId, cancellationToken);
                    statistics.RecordMessage();
                    await commandRouter.HandleAsync(
                        update.PeerId,
                        update.UserId,
                        update.Text,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Failed to handle VK update: {exception.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static BotMessageUpdate? ParseMessageUpdate(JsonElement update)
    {
        if (!update.TryGetProperty("type", out var type) || type.GetString() != "message_new" ||
            !update.TryGetProperty("object", out var payload))
        {
            return null;
        }

        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("message", out var nestedMessage))
        {
            payload = nestedMessage;
        }

        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (payload.TryGetProperty("out", out var outgoing) &&
            (outgoing.ValueKind == JsonValueKind.True ||
             outgoing.ValueKind == JsonValueKind.Number && outgoing.GetInt32() != 0))
        {
            return null;
        }

        if (!payload.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String ||
            !payload.TryGetProperty("peer_id", out var peerId) || peerId.ValueKind != JsonValueKind.Number ||
            !payload.TryGetProperty("from_id", out var userId) || userId.ValueKind != JsonValueKind.Number)
        {
            Console.Error.WriteLine("Received message_new event without text, peer_id or from_id.");
            return null;
        }

        return new BotMessageUpdate(peerId.GetInt64(), userId.GetInt64(), text.GetString() ?? "");
    }
}

internal readonly record struct BotMessageUpdate(long PeerId, long UserId, string Text);