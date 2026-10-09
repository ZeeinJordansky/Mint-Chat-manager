namespace MintChatManager;

public interface IBotCommand
{
    string Name { get; }
    string Description { get; }
    Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken);
}

public readonly record struct BotCommandContext(long PeerId, long UserId);

public sealed class CommandRouter
{
    private readonly VkApiClient api;
    private readonly BotStatistics statistics;
    private readonly IReadOnlyDictionary<string, IBotCommand> commands;

    public CommandRouter(VkApiClient api, IEnumerable<IBotCommand> commands, BotStatistics statistics)
    {
        this.api = api;
        this.statistics = statistics;
        this.commands = commands.ToDictionary(command => command.Name, StringComparer.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(
        long peerId,
        long userId,
        string text,
        CancellationToken cancellationToken)
    {
        var commandText = text.AsSpan().TrimStart();
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
            statistics.RecordCommand();
            await command.ExecuteAsync(
                api,
                new BotCommandContext(peerId, userId),
                separator < 0 ? "" : commandText[(separator + 1)..].TrimStart().ToString(),
                cancellationToken);
        }
        else
        {
            Console.WriteLine($"Unknown command {commandName} from peer {peerId}.");
        }
    }
}