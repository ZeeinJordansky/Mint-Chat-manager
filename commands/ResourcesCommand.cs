using System.Diagnostics;

namespace MintChatManager.Commands;

public sealed class ResourcesCommand : IBotCommand
{
    public string Name => "/ресурсы";
    public string Description => "Показать использование памяти ботом";

    public Task ExecuteAsync(
        VkApiClient api,
        BotCommandContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = Process.GetCurrentProcess();
        var workingSet = (ulong)process.WorkingSet64;
        var managedHeap = (ulong)GC.GetTotalMemory(false);
        var message =
            $"Память бота\n" +
            $"Рабочий набор: {SystemMetrics.FormatBytes(workingSet)}\n" +
            $"Управляемая куча: {SystemMetrics.FormatBytes(managedHeap)}";
        return api.SendMessageAsync(context.PeerId, message, cancellationToken);
    }
}
