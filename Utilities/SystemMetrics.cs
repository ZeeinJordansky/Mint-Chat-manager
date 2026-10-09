using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MintChatManager.Commands;

internal static class SystemMetrics
{
    public static (ulong Used, ulong Free, ulong Total) GetMemory()
    {
        if (OperatingSystem.IsLinux())
        {
            return GetLinuxMemory();
        }

        if (OperatingSystem.IsWindows())
        {
            return GetWindowsMemory();
        }

        throw new PlatformNotSupportedException("System memory metrics are supported on Linux and Windows.");
    }

    public static double GetAverageCpuUsage()
    {
        using var process = Process.GetCurrentProcess();
        var elapsedMilliseconds = (DateTimeOffset.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
        if (elapsedMilliseconds <= 0)
        {
            return 0;
        }

        return process.TotalProcessorTime.TotalMilliseconds /
               (elapsedMilliseconds * Environment.ProcessorCount) * 100;
    }

    public static string FormatBytes(ulong bytes)
    {
        const double gibibyte = 1024 * 1024 * 1024;
        const double mebibyte = 1024 * 1024;

        return bytes >= gibibyte
            ? $"{bytes / gibibyte:N1} ГиБ"
            : $"{bytes / mebibyte:N0} МиБ";
    }

    private static (ulong Used, ulong Free, ulong Total) GetLinuxMemory()
    {
        ulong total = 0;
        ulong available = 0;

        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            var separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var name = line[..separator];
            if (name is not ("MemTotal" or "MemAvailable"))
            {
                continue;
            }

            var fields = line[(separator + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2 ||
                !ulong.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var kibibytes) ||
                fields[1] != "kB")
            {
                throw new FormatException($"Invalid {name} value in /proc/meminfo.");
            }

            var bytes = kibibytes * 1024;
            if (name == "MemTotal")
            {
                total = bytes;
            }
            else
            {
                available = bytes;
            }
        }

        if (total == 0 || available > total)
        {
            throw new InvalidOperationException("Could not read total and available memory from /proc/meminfo.");
        }

        return (total - available, available, total);
    }

    private static (ulong Used, ulong Free, ulong Total) GetWindowsMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new InvalidOperationException("Could not read system memory status.");
        }

        return (
            status.TotalPhysicalMemory - status.AvailablePhysicalMemory,
            status.AvailablePhysicalMemory,
            status.TotalPhysicalMemory);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysicalMemory;
        public ulong AvailablePhysicalMemory;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
