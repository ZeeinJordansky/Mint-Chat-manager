using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MintChatManager.Commands;

internal static class StatsImageGenerator
{
    private const int Width = 800;
    private const int Height = 440;
    private const int Channels = 3;
    private static readonly uint[] Crc32Table = CreateCrc32Table();
    private static readonly Dictionary<char, string[]> Font = new()
    {
        [' '] = ["00000", "00000", "00000", "00000", "00000", "00000", "00000"],
        ['-'] = ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
        [','] = ["00000", "00000", "00000", "00000", "00000", "00100", "01000"],
        [':'] = ["00000", "00100", "00100", "00000", "00100", "00100", "00000"],
        ['.'] = ["00000", "00000", "00000", "00000", "00000", "00100", "00100"],
        ['%'] = ["11001", "11010", "00100", "01000", "10110", "00110", "00000"],
        ['/'] = ["00001", "00010", "00010", "00100", "01000", "01000", "10000"],
        ['0'] = ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
        ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['2'] = ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
        ['3'] = ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
        ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
        ['5'] = ["11111", "10000", "10000", "11110", "00001", "00001", "11110"],
        ['6'] = ["01110", "10000", "10000", "11110", "10001", "10001", "01110"],
        ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
        ['9'] = ["01110", "10001", "10001", "01111", "00001", "00001", "01110"],
        ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['B'] = ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
        ['C'] = ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
        ['D'] = ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
        ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
        ['F'] = ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
        ['G'] = ["01111", "10000", "10000", "10111", "10001", "10001", "01111"],
        ['H'] = ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['I'] = ["01110", "00100", "00100", "00100", "00100", "00100", "01110"],
        ['J'] = ["00111", "00010", "00010", "00010", "00010", "10010", "01100"],
        ['K'] = ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
        ['L'] = ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
        ['M'] = ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
        ['N'] = ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
        ['O'] = ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['P'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
        ['Q'] = ["01110", "10001", "10001", "10001", "10101", "10010", "01101"],
        ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
        ['S'] = ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
        ['T'] = ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
        ['U'] = ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['V'] = ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
        ['W'] = ["10001", "10001", "10001", "10101", "10101", "10101", "01010"],
        ['X'] = ["10001", "10001", "01010", "00100", "01010", "10001", "10001"],
        ['Y'] = ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
        ['Z'] = ["11111", "00001", "00010", "00100", "01000", "10000", "11111"]
    };

    public static byte[] Generate(
        long messages,
        long commands,
        TimeSpan uptime,
        double cpuPercent,
        ulong memoryUsed,
        ulong memoryTotal)
    {
        var pixels = new byte[Width * Height * Channels];
        DrawBackground(pixels);
        DrawCircle(pixels, 720, 108, 104, 2, 35, 85, 106);
        DrawCircle(pixels, 720, 108, 78, 2, 35, 85, 106);
        DrawCircle(pixels, 720, 108, 52, 2, 35, 85, 106);
        Fill(pixels, 0, 0, Width, 5, 49, 218, 174);
        DrawText(pixels, 40, 30, "MINT CHAT", 4, 72, 235, 195, 210);
        DrawText(pixels, 40, 78, "BOT OVERVIEW", 5, 245, 248, 255, 410);
        DrawText(pixels, 42, 123, DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm"), 2, 153, 171, 195, 240);
        Fill(pixels, 40, 161, 72, 3, 105, 125, 255);
        Fill(pixels, 116, 161, 40, 3, 49, 218, 174);

        DrawCard(pixels, 40, 185, 345, 100, "TOTAL MESSAGES", messages.ToString("N0"), 49, 218, 174);
        DrawCard(pixels, 415, 185, 345, 100, "COMMANDS RUN", commands.ToString("N0"), 124, 123, 255);
        DrawCard(pixels, 40, 305, 345, 100, "UPTIME", FormatUptime(uptime), 255, 183, 93);
        DrawCard(pixels, 415, 305, 345, 100, "CPU / MEMORY", $"{cpuPercent:F1}% / {FormatMemory(memoryUsed)}", 49, 218, 174);

        var memoryBarWidth = memoryTotal == 0
            ? 0
            : (int)Math.Clamp((double)memoryUsed / memoryTotal * 276, 0, 276);
        Fill(pixels, 449, 379, 276, 7, 47, 58, 79);
        Fill(pixels, 449, 379, memoryBarWidth, 7, 49, 218, 174);

        return EncodePng(pixels);
    }

    private static void DrawBackground(byte[] pixels)
    {
        for (var y = 0; y < Height; y++)
        {
            var blend = y / (double)(Height - 1);
            var red = (byte)(12 + blend * 8);
            var green = (byte)(19 + blend * 9);
            var blue = (byte)(34 + blend * 18);
            Fill(pixels, 0, y, Width, 1, red, green, blue);
        }
    }

    private static void DrawCircle(byte[] pixels, int centerX, int centerY, int radius, int thickness, byte red, byte green, byte blue)
    {
        var outerRadiusSquared = radius * radius;
        var innerRadius = radius - thickness;
        var innerRadiusSquared = innerRadius * innerRadius;
        for (var y = Math.Max(centerY - radius, 0); y < Math.Min(centerY + radius, Height); y++)
        {
            for (var x = Math.Max(centerX - radius, 0); x < Math.Min(centerX + radius, Width); x++)
            {
                var distanceSquared = (x - centerX) * (x - centerX) + (y - centerY) * (y - centerY);
                if (distanceSquared <= outerRadiusSquared && distanceSquared >= innerRadiusSquared)
                {
                    Fill(pixels, x, y, 1, 1, red, green, blue);
                }
            }
        }
    }

    private static void DrawCard(
        byte[] pixels,
        int x,
        int y,
        int width,
        int height,
        string label,
        string value,
        byte accentRed,
        byte accentGreen,
        byte accentBlue)
    {
        Fill(pixels, x + 3, y + 5, width, height, 8, 13, 24);
        Fill(pixels, x, y, width, height, 27, 38, 57);
        Fill(pixels, x, y + 8, 3, height - 16, accentRed, accentGreen, accentBlue);
        Fill(pixels, x + 3, y, width - 6, 1, 48, 62, 81);
        DrawText(pixels, x + 22, y + 15, label, 2, 151, 169, 190, width - 40);
        DrawText(pixels, x + 22, y + 45, value, 3, 243, 247, 255, width - 40);
    }

    private static void DrawText(
        byte[] pixels,
        int x,
        int y,
        string text,
        int scale,
        byte red,
        byte green,
        byte blue,
        int maxWidth)
    {
        var cursor = x;
        foreach (var character in text.ToUpperInvariant())
        {
            if (Font.TryGetValue(character, out var glyph))
            {
                for (var row = 0; row < glyph.Length; row++)
                {
                    var column = 0;
                    while (column < glyph[row].Length)
                    {
                        if (glyph[row][column] != '1')
                        {
                            column++;
                            continue;
                        }

                        var start = column++;
                        while (column < glyph[row].Length && glyph[row][column] == '1')
                        {
                            column++;
                        }

                        Fill(
                            pixels,
                            cursor + start * scale,
                            y + row * scale,
                            (column - start) * scale,
                            scale,
                            red,
                            green,
                            blue);
                    }
                }
            }

            cursor += 6 * scale;
            if (cursor >= x + maxWidth)
            {
                break;
            }
        }
    }

    private static void Fill(
        byte[] pixels,
        int x,
        int y,
        int width,
        int height,
        byte red,
        byte green,
        byte blue)
    {
        var right = Math.Min(x + width, Width);
        var bottom = Math.Min(y + height, Height);
        for (var row = Math.Max(y, 0); row < bottom; row++)
        {
            var offset = (row * Width + Math.Max(x, 0)) * Channels;
            for (var column = Math.Max(x, 0); column < right; column++)
            {
                pixels[offset++] = red;
                pixels[offset++] = green;
                pixels[offset++] = blue;
            }
        }
    }

    private static byte[] EncodePng(byte[] pixels)
    {
        using var compressed = new MemoryStream(Width * Height / 8);
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (var row = 0; row < Height; row++)
            {
                zlib.WriteByte(0);
                zlib.Write(pixels, row * Width * Channels, Width * Channels);
            }
        }

        using var png = new MemoryStream(checked((int)compressed.Length + 64));
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], Height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        Span<byte> typeBytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(type, typeBytes);
        output.Write(typeBytes);
        output.Write(data);

        var crc = uint.MaxValue;
        foreach (var value in typeBytes)
        {
            crc = Crc32Table[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        foreach (var value in data)
        {
            crc = Crc32Table[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, ~crc);
        output.Write(checksum);
    }

    private static uint[] CreateCrc32Table()
    {
        var table = new uint[256];
        for (var value = 0; value < table.Length; value++)
        {
            var crc = (uint)value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : 0xedb88320 ^ (crc >> 1);
            }

            table[value] = crc;
        }

        return table;
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        return uptime.Days == 0
            ? $"{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}"
            : $"{uptime.Days}D {uptime.Hours:D2}:{uptime.Minutes:D2}";
    }

    private static string FormatMemory(ulong bytes)
    {
        const double mebibyte = 1024 * 1024;
        return $"{bytes / mebibyte:F0}MB";
    }
}
