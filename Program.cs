using MintChatManager;
using MintChatManager.Commands;
using Npgsql;

LoadDotEnv();

var accessToken = Environment.GetEnvironmentVariable("VK_ACCESS_TOKEN");
var groupIdValue = Environment.GetEnvironmentVariable("VK_GROUP_ID") ?? "232664735";
var postgresPassword = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

if (string.IsNullOrWhiteSpace(accessToken) ||
    string.IsNullOrWhiteSpace(postgresPassword) ||
    !long.TryParse(groupIdValue, out var groupId))
{
    Console.Error.WriteLine(
        "Set VK_ACCESS_TOKEN and POSTGRES_PASSWORD, and provide a valid VK_GROUP_ID.");
    return 1;
}

var lockPath = Path.Combine(Path.GetTempPath(), $"mint-chat-manager-{groupId}.lock");
FileStream instanceLock;
try
{
    instanceLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
}
catch (IOException)
{
    Console.Error.WriteLine($"A bot instance for group {groupId} is already running.");
    return 1;
}

using var botInstanceLock = instanceLock;

var adminIdsValue = Environment.GetEnvironmentVariable("BOT_ADMIN_IDS") ?? "778382713";
var adminIds = adminIdsValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
    .Select(value => long.TryParse(value, out var id) && id > 0
        ? id
        : throw new FormatException("BOT_ADMIN_IDS must contain comma-separated positive user IDs."))
    .ToHashSet();
if (adminIds.Count == 0)
{
    throw new FormatException("Configure at least one administrator in BOT_ADMIN_IDS.");
}

var postgresPortValue = Environment.GetEnvironmentVariable("POSTGRES_PORT");
var postgresPort = 5432;
if (!string.IsNullOrWhiteSpace(postgresPortValue) &&
    (!int.TryParse(postgresPortValue, out postgresPort) || postgresPort is < 1 or > 65535))
{
    throw new FormatException("POSTGRES_PORT must be a valid TCP port from 1 to 65535.");
}

var postgresConnectionString = new NpgsqlConnectionStringBuilder
{
    Host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost",
    Port = postgresPort,
    Database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "mintchatmanager",
    Username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "mintbot",
    Password = postgresPassword,
    MaxPoolSize = 8
}.ConnectionString;

await using var dataSource = NpgsqlDataSource.Create(postgresConnectionString);
var store = new PostgresStore(dataSource);
await store.InitializeAsync(CancellationToken.None);

using var httpClient = new HttpClient(new SocketsHttpHandler
{
    MaxConnectionsPerServer = 8,
    PooledConnectionLifetime = TimeSpan.FromMinutes(10)
})
{
    Timeout = TimeSpan.FromSeconds(40)
};
var api = new VkApiClient(httpClient, accessToken);
var statistics = new BotStatistics();
var commands = new List<IBotCommand>
{
    new PingCommand(),
    new IdCommand(),
    new ProfileCommand(store, adminIds),
    new WarnCommand(store, adminIds),
    new UnwarnCommand(store, adminIds),
    new MuteCommand(store, adminIds),
    new UnmuteCommand(store, adminIds),
    new KickCommand(store, adminIds),
    new SilentKickCommand(store, adminIds),
    new BanCommand(store, adminIds),
    new UnbanCommand(store, adminIds),
    new ListsCommand(store, adminIds),
    new AddAccessLevelCommand(store, adminIds),
    new RemoveRoleCommand(store, adminIds),
    new StaffListCommand(store),
    new GetRoleCommand(store, adminIds),
    new PinCommand(store, adminIds),
    new UnpinCommand(store, adminIds),
    new BalanceCommand(store),
    new BitcoinRateCommand(),
    new BuyBitcoinCommand(store),
    new SellBitcoinCommand(store),
    new BankDepositCommand(store),
    new BankWithdrawCommand(store),
    new TransferMoneyCommand(store),
    new ConfirmTransferCommand(store),
    new CancelTransferCommand(),
    new DailyBonusCommand(store),
    new ClaimDailyBonusCommand(store)
};
commands.Add(new HelpCommand(commands));
var router = new CommandRouter(api, commands, statistics, store);
var bot = new LongPollBot(api, router, groupId, statistics, store);
using var cancellationTokenSource = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();
};

Console.WriteLine($"VK Long Poll bot is running for group {groupId}. Press Ctrl+C to stop.");
await bot.RunAsync(cancellationTokenSource.Token);
return 0;

static void LoadDotEnv()
{
    var envFile = Path.Combine(Directory.GetCurrentDirectory(), "Configuration", ".env");
    if (!File.Exists(envFile))
    {
        envFile = Path.Combine(AppContext.BaseDirectory, "Configuration", ".env");
        if (!File.Exists(envFile))
        {
            return;
        }
    }


    foreach (var line in File.ReadLines(envFile))
    {
        var entry = line.Trim();
        if (entry.Length == 0 || entry.StartsWith('#'))
        {
            continue;
        }

        var separator = entry.IndexOf('=');
        if (separator <= 0)
        {
            throw new FormatException($"Invalid entry in Configuration/.env; expected KEY=VALUE.");
        }

        var name = entry[..separator].Trim();
        var value = entry[(separator + 1)..].Trim();
        if (value.Length >= 2 &&
            (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
        {
            value = value[1..^1];
        }

        if (Environment.GetEnvironmentVariable(name) is null)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}