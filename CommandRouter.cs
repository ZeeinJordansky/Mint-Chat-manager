namespace MintChatManager;

public interface IBotCommand
{
    string Name { get; }
    string Description { get; }
    decimal MinimumRole => 0;
    IReadOnlyCollection<string> Aliases => Array.Empty<string>();
    Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken);
}

public interface IInternalBotCommand
{
}

public readonly record struct BotCommandContext(
    long PeerId,
    long UserId,
    long? MessageId,
    long? ReplyToUserId = null,
    long? ReplyToConversationMessageId = null,
    long? ConversationMessageId = null,
    string CommandName = "",
    bool IsCallback = false)
{
    public Task SendMessageAsync(
        VkApiClient api,
        string message,
        CancellationToken cancellationToken,
        bool quoteCommand = true,
        bool disableMentions = false)
    {
        return api.SendMessageAsync(
            PeerId,
            message,
            cancellationToken,
            replyToConversationMessageId: quoteCommand ? ConversationMessageId : null,
            disableMentions: disableMentions);
    }

    public Task SendPhotoAsync(
        VkApiClient api,
        byte[] image,
        string caption,
        CancellationToken cancellationToken,
        string? keyboard = null,
        bool disableMentions = false)
    {
        return api.SendPhotoAsync(
            PeerId,
            image,
            caption,
            cancellationToken,
            replyToConversationMessageId: ConversationMessageId,
            keyboard: keyboard,
            disableMentions: disableMentions);
    }

    public Task SendMessageWithAttachmentAsync(
        VkApiClient api,
        string message,
        string attachment,
        CancellationToken cancellationToken,
        bool quoteCommand = true,
        bool disableMentions = false,
        string? keyboard = null)
    {
        return api.SendMessageWithAttachmentAsync(
            PeerId,
            message,
            attachment,
            cancellationToken,
            replyToConversationMessageId: quoteCommand ? ConversationMessageId : null,
            disableMentions: disableMentions,
            keyboard: keyboard);
    }

    public Task SendMessageWithKeyboardAsync(
        VkApiClient api,
        string message,
        string keyboard,
        CancellationToken cancellationToken,
        bool quoteCommand = true,
        bool disableMentions = false)
    {
        return api.SendMessageWithKeyboardAsync(
            PeerId,
            message,
            keyboard,
            cancellationToken,
            replyToConversationMessageId: quoteCommand ? ConversationMessageId : null,
            disableMentions: disableMentions);
    }

    public Task EditMessageAsync(
        VkApiClient api,
        string message,
        string? keyboard,
        CancellationToken cancellationToken)
    {
        return EditMessageCoreAsync(api, message, keyboard, cancellationToken);
    }

    private async Task EditMessageCoreAsync(
        VkApiClient api,
        string message,
        string? keyboard,
        CancellationToken cancellationToken)
    {
        var messageId = MessageId;
        if (messageId is not > 0 && ConversationMessageId is > 0)
        {
            messageId = await api.GetMessageIdByConversationMessageIdAsync(
                PeerId, ConversationMessageId.Value, cancellationToken);
        }

        if (messageId is not > 0)
        {
            throw new InvalidOperationException(
                $"VK did not provide a message ID for callback message {ConversationMessageId?.ToString() ?? "unknown"}.");
        }

        await api.EditMessageAsync(PeerId, messageId.Value, message, keyboard, cancellationToken);
    }
}

public sealed class CommandRouter
{
    private readonly VkApiClient api;
    private readonly BotStatistics statistics;
    private readonly PostgresStore store;
    private readonly Dictionary<string, IBotCommand> commands;

    public CommandRouter(
        VkApiClient api,
        IEnumerable<IBotCommand> commands,
        BotStatistics statistics,
        PostgresStore store)
    {
        this.api = api;
        this.statistics = statistics;
        this.store = store;
        this.commands = new Dictionary<string, IBotCommand>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in commands)
        {
            foreach (var name in GetCommandNames(command))
            {
                if (this.commands.ContainsKey(name))
                {
                    throw new InvalidOperationException($"Duplicate command registration for '{name}'.");
                }

                this.commands[name] = command;
            }
        }
    }

    private static IEnumerable<string> GetCommandNames(IBotCommand command)
    {
        var names = new List<string> { command.Name };
        if (command.Aliases is not null)
        {
            names.AddRange(command.Aliases.Where(name => !string.IsNullOrWhiteSpace(name)));
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(
        long peerId,
        long userId,
        long? messageId,
        long? replyToUserId,
        long? replyToConversationMessageId,
        long? conversationMessageId,
        bool isCallback,
        string text,
        CancellationToken cancellationToken)
    {
        var normalizedText = text.TrimStart();
        ChatSettings? chatSettings = null;
        var hasSlashPrefix = normalizedText.StartsWith('/');
        var hasAlternatePrefix = normalizedText.Length > 0 && "!.,+*;:".Contains(normalizedText[0]);
        var firstWord = normalizedText.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        var commandCandidate = firstWord is null
            ? null
            : "/" + firstWord.TrimStart('/', '!', '.', ',', '+', '*', ';', ':');
        var isKnownPrefixlessCommand = commandCandidate is not null && commands.ContainsKey(commandCandidate);

        if (!hasSlashPrefix && !hasAlternatePrefix && !isKnownPrefixlessCommand)
        {
            return;
        }

        if (peerId > 2_000_000_000)
        {
            chatSettings = await store.GetChatSettingsAsync(peerId, cancellationToken);
            if (!hasSlashPrefix && !hasAlternatePrefix)
            {
                if (!chatSettings.NoPrefix)
                {
                    return;
                }

                normalizedText = "/" + normalizedText;
            }
        }

        if (hasAlternatePrefix)
        {
            normalizedText = "/" + normalizedText[1..].TrimStart();
        }
        else if (!hasSlashPrefix)
        {
            normalizedText = "/" + normalizedText;
        }

        var commandText = normalizedText.AsSpan();
        if (commandText.IsEmpty || commandText[0] != '/')
        {
            return;
        }

        var separator = commandText.IndexOfAny(" \t\r\n");
        var commandLength = separator < 0 ? commandText.Length : separator;
        var mention = commandText[..commandLength].IndexOf('@');
        if (mention >= 0)
        {
            commandLength = mention;
        }

        var commandName = commandText[..commandLength].ToString();
        if (commands.TryGetValue(commandName, out var command))
        {
            Console.WriteLine($"Running {commandName} for peer {peerId}.");
            if (command is IInternalBotCommand && !isCallback)
            {
                await new BotCommandContext(
                        peerId, userId, messageId, replyToUserId, replyToConversationMessageId, conversationMessageId,
                        commandName, isCallback)
                    .SendMessageAsync(
                        api,
                        "Эта служебная команда доступна только через кнопку в сообщении бота.",
                        cancellationToken);
                return;
            }

            if (chatSettings is { IsActive: false } && command.Name is not ("/start" or "/help"))
            {
                await new BotCommandContext(
                        peerId, userId, messageId, replyToUserId, replyToConversationMessageId, conversationMessageId,
                        commandName, isCallback)
                    .SendMessageAsync(
                        api,
                        "Бот в этой беседе ещё не активирован. Владелец беседы может включить его командой /start.",
                        cancellationToken);
                return;
            }

            statistics.RecordCommand();
            await command.ExecuteAsync(
                api,
                new BotCommandContext(
                    peerId, userId, messageId, replyToUserId, replyToConversationMessageId, conversationMessageId,
                    commandName, isCallback),
                separator < 0 ? "" : commandText[(separator + 1)..].TrimStart().ToString(),
                cancellationToken);
        }
        else
        {
            Console.WriteLine($"Unknown command {commandName} from peer {peerId}.");
        }
    }
}