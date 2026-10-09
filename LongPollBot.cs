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
        var updateChannel = Channel.CreateBounded<BotUpdate>(new BoundedChannelOptions(64)
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
        ChannelWriter<BotUpdate> updateWriter,
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
                        var botUpdate = ParseUpdate(update);
                        if (botUpdate is { } parsedUpdate)
                        {
                            await updateWriter.WriteAsync(parsedUpdate, cancellationToken);
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
        ChannelReader<BotUpdate> updateReader,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in updateReader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    if (update.EventId is not null)
                    {
                        await api.AnswerMessageEventAsync(
                            update.PeerId,
                            update.UserId,
                            update.EventId,
                            cancellationToken);
                    }
                    else
                    {
                        await store.RecordMessageAsync(update.UserId, cancellationToken);
                        statistics.RecordMessage();
                    }

                    await commandRouter.HandleAsync(
                        update.PeerId,
                        update.UserId,
                        update.MessageId,
                        update.ReplyToUserId,
                        update.ReplyToConversationMessageId,
                        update.ConversationMessageId,
                        update.EventId is not null,
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

    private static BotUpdate? ParseUpdate(JsonElement update)
    {
        if (!update.TryGetProperty("type", out var type) ||
            !update.TryGetProperty("object", out var payload))
        {
            return null;
        }

        if (type.GetString() == "message_event")
        {
            return ParseCallbackUpdate(payload);
        }

        if (type.GetString() != "message_new")
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

        long? messageId = payload.TryGetProperty("id", out var id) &&
                          id.ValueKind == JsonValueKind.Number &&
                          id.TryGetInt64(out var parsedMessageId) &&
                          parsedMessageId > 0
            ? parsedMessageId
            : null;
        long? conversationMessageId = payload.TryGetProperty("conversation_message_id", out var cmid) &&
                                      cmid.ValueKind == JsonValueKind.Number &&
                                      cmid.TryGetInt64(out var parsedCmid) &&
                                      parsedCmid > 0
            ? parsedCmid
            : null;

        long? replyToUserId = null;
        long? replyToConversationMessageId = null;
        if (payload.TryGetProperty("reply_message", out var reply) &&
            reply.ValueKind == JsonValueKind.Object)
        {
            if (reply.TryGetProperty("from_id", out var replyFromId) &&
                replyFromId.ValueKind == JsonValueKind.Number &&
                replyFromId.TryGetInt64(out var parsedReplyUserId))
            {
                replyToUserId = parsedReplyUserId;
            }

            if (reply.TryGetProperty("conversation_message_id", out var replyCmid) &&
                replyCmid.ValueKind == JsonValueKind.Number &&
                replyCmid.TryGetInt64(out var parsedReplyCmid) &&
                parsedReplyCmid > 0)
            {
                replyToConversationMessageId = parsedReplyCmid;
            }
        }
        else if (payload.TryGetProperty("fwd_messages", out var forwarded) &&
                 forwarded.ValueKind == JsonValueKind.Array &&
                 forwarded.GetArrayLength() > 0 &&
                 forwarded[0].TryGetProperty("conversation_message_id", out var forwardedCmid) &&
                 forwardedCmid.ValueKind == JsonValueKind.Number &&
                 forwardedCmid.TryGetInt64(out var parsedForwardedCmid) &&
                 parsedForwardedCmid > 0)
        {
            replyToConversationMessageId = parsedForwardedCmid;
        }

        return new BotUpdate(
            peerId.GetInt64(),
            userId.GetInt64(),
            messageId,
            text.GetString() ?? "",
            replyToUserId,
            replyToConversationMessageId,
            conversationMessageId);
    }

    private static BotUpdate? ParseCallbackUpdate(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("peer_id", out var peerId) || peerId.ValueKind != JsonValueKind.Number ||
            !payload.TryGetProperty("user_id", out var userId) || userId.ValueKind != JsonValueKind.Number ||
            !payload.TryGetProperty("event_id", out var eventId) || eventId.ValueKind != JsonValueKind.String ||
            !payload.TryGetProperty("payload", out var buttonPayload) ||
            buttonPayload.ValueKind != JsonValueKind.Object ||
            !buttonPayload.TryGetProperty("command", out var command) ||
            command.ValueKind != JsonValueKind.String)
        {
            Console.Error.WriteLine("Received message_event without peer_id, user_id, event_id or command payload.");
            return null;
        }

        long? messageId = payload.TryGetProperty("message_id", out var id) &&
                          id.ValueKind == JsonValueKind.Number &&
                          id.TryGetInt64(out var parsedMessageId) &&
                          parsedMessageId > 0
            ? parsedMessageId
            : null;

        return new BotUpdate(
            peerId.GetInt64(),
            userId.GetInt64(),
            messageId,
            command.GetString() ?? "",
            EventId: eventId.GetString());
    }
}

internal readonly record struct BotUpdate(
    long PeerId,
    long UserId,
    long? MessageId,
    string Text,
    long? ReplyToUserId = null,
    long? ReplyToConversationMessageId = null,
    long? ConversationMessageId = null,
    string? EventId = null);