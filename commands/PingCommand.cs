using System.Diagnostics;

namespace MintChatManager.Commands;

public sealed class PingCommand : IBotCommand
{
    public string Name => "/ping";
    public string Description => "Проверить отклик и показать нагрузку";
    public IReadOnlyCollection<string> Aliases => new[] { "/пинг" };

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
            "┌─ 🛰️ Проверка отклика бота\n" +
            $"│ Пинг: {pingMilliseconds:F1} мс\n" +
            $"│ ОЗУ: {SystemMetrics.FormatBytes(memory.Used)} занято / {SystemMetrics.FormatBytes(memory.Free)} свободно\n" +
            $"│ Всего: {SystemMetrics.FormatBytes(memory.Total)}\n" +
            $"│ CPU: {cpuUsage:F1}% средняя нагрузка ({Environment.ProcessorCount} ядер)\n" +
            "└─────────────────────────────";

        return context.SendMessageAsync(api, message, cancellationToken);
    }
}