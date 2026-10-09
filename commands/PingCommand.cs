using System.Diagnostics;

namespace MintChatManager.Commands;

public sealed class PingCommand : IBotCommand
{
    public string Name => "/ping";
    public string Description => "Проверить отклик и показать нагрузку";

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var memory = SystemMetrics.GetMemory();
        var cpuUsage = SystemMetrics.GetAverageCpuUsage();
        var pingMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

        var message =
            $"pong\n" +
            $"Пинг (обработка): {pingMilliseconds:F1} ms\n" +
            $"ОЗУ: занято {SystemMetrics.FormatBytes(memory.Used)}, свободно {SystemMetrics.FormatBytes(memory.Free)} " +
            $"(всего {SystemMetrics.FormatBytes(memory.Total)})\n" +
            $"ЦПУ бота: {cpuUsage:F1}% в среднем с запуска ({Environment.ProcessorCount} ядер)";

        return api.SendMessageAsync(context.PeerId, message, cancellationToken);
    }
}