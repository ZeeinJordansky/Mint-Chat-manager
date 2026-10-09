namespace MintChatManager.Commands;

public static class SupportTicketNotifier
{
    public static async Task SendAsync(
        VkApiClient api,
        long? supportChatPeerId,
        IReadOnlySet<long> administratorIds,
        string message,
        string? keyboard,
        CancellationToken cancellationToken)
    {
        VkApiException? lastError = null;
        if (supportChatPeerId is > 2_000_000_000)
        {
            try
            {
                if (keyboard is null)
                {
                    await api.SendMessageAsync(supportChatPeerId.Value, message, cancellationToken);
                }
                else
                {
                    await api.SendMessageWithKeyboardAsync(
                        supportChatPeerId.Value, message, keyboard, cancellationToken);
                }

                return;
            }
            catch (VkApiException exception)
            {
                lastError = exception;
                Console.Error.WriteLine($"Configured support chat notification failed: {exception}");
            }
        }

        var delivered = 0;
        foreach (var administratorId in administratorIds)
        {
            try
            {
                if (keyboard is null)
                {
                    await api.SendMessageAsync(administratorId, message, cancellationToken);
                }
                else
                {
                    await api.SendMessageWithKeyboardAsync(
                        administratorId, message, keyboard, cancellationToken);
                }

                delivered++;
            }
            catch (VkApiException exception)
            {
                lastError = exception;
                Console.Error.WriteLine(
                    $"Support notification to administrator {administratorId} failed: {exception}");
            }
        }

        if (delivered == 0)
        {
            throw new SupportTicketNotificationException(
                "Could not notify the support conversation or any configured administrator.",
                lastError);
        }
    }
}

public sealed class SupportTicketNotificationException(string message, Exception? innerException)
    : Exception(message, innerException);
