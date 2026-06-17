using System;
using System.Collections.Generic;
using System.Text;

namespace FmlDiff.Services;

public static class HexByteSelection
{
    public static byte[] ExtractBytesFromCellRange(DiffByteCell[] cells, int startCell, int endCell)
    {
        if (cells == null || cells.Length == 0)
            return Array.Empty<byte>();

        int lo = Math.Min(startCell, endCell);
        int hi = Math.Max(startCell, endCell);
        var bytes = new List<byte>();

        for (int i = lo; i <= hi; i++)
        {
            if (i < cells.Length && cells[i].HasValue)
                bytes.Add(cells[i].Value);
        }

        return bytes.ToArray();
    }

    public static string FormatBytesAsHex(IReadOnlyList<byte> bytes)
    {
        if (bytes == null || bytes.Count == 0)
            return string.Empty;

        var text = new StringBuilder(bytes.Count * 3 - 1);
        for (int i = 0; i < bytes.Count; i++)
        {
            if (i > 0)
                text.Append(' ');

            text.Append(bytes[i].ToString("X2"));
        }

        return text.ToString();
    }

    public static bool IsCellInRange(int cellIndex, int startCell, int endCell)
    {
        int lo = Math.Min(startCell, endCell);
        int hi = Math.Max(startCell, endCell);
        return cellIndex >= lo && cellIndex <= hi;
    }
}
