namespace MintChatManager.Commands;

public sealed class StatsCommand(BotStatistics statistics) : IBotCommand
{
    public string Name => "/стата";
    public string Description => "Показать статистику и состояние бота";

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var memory = SystemMetrics.GetMemory();
        var cpuUsage = SystemMetrics.GetAverageCpuUsage();
        var uptime = statistics.Uptime;
        var message =
            $"Статистика бота\n" +
            $"Работает: {uptime.Days} д. {uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}\n" +
            $"Обработано сообщений: {statistics.ProcessedMessages}\n" +
            $"Выполнено команд: {statistics.ExecutedCommands}\n" +
            $"ОЗУ: занято {SystemMetrics.FormatBytes(memory.Used)}, свободно {SystemMetrics.FormatBytes(memory.Free)} " +
            $"(всего {SystemMetrics.FormatBytes(memory.Total)})\n" +
            $"ЦПУ бота: {cpuUsage:F1}% в среднем";

        return SendStatisticsAsync(api, context.PeerId, message, statistics, memory, cpuUsage, uptime, cancellationToken);
    }

    private static async Task SendStatisticsAsync(
        VkApiClient api,
        long peerId,
        string message,
        BotStatistics statistics,
        (ulong Used, ulong Free, ulong Total) memory,
        double cpuUsage,
        TimeSpan uptime,
        CancellationToken cancellationToken)
    {
        await api.SendMessageAsync(peerId, message, cancellationToken);
        var image = StatsImageGenerator.Generate(
            statistics.ProcessedMessages,
            statistics.ExecutedCommands,
            uptime,
            cpuUsage,
            memory.Used,
            memory.Total);
        await api.SendPhotoAsync(peerId, image, "Статистика бота", cancellationToken);
    }
}
