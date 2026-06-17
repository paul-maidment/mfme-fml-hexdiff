using System;
using System.Collections.Generic;
using System.Text;

namespace FmlDiff.Services;

public enum HexSearchMode
{
    Utf8,
    Utf16Le,
    ByteSequence
}

public enum HexSearchPane
{
    PaneA,
    PaneB
}

public readonly record struct HexSearchMatch(int FileOffset, int Length);

public static class HexPaneSearcher
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static bool TryBuildPattern(string query, HexSearchMode mode, out byte[] pattern, out string error)
    {
        pattern = null;
        error = null;

        if (string.IsNullOrEmpty(query))
        {
            error = "Enter a search term.";
            return false;
        }

        switch (mode)
        {
            case HexSearchMode.Utf8:
                pattern = Utf8.GetBytes(query);
                return true;

            case HexSearchMode.Utf16Le:
                pattern = EncodeUtf16Le(query);
                return true;

            case HexSearchMode.ByteSequence:
                return TryParseByteSequence(query, out pattern, out error);

            default:
                error = "Unknown search mode.";
                return false;
        }
    }

    public static byte[] ExtractPaneBytes(DiffByteCell[] cells)
    {
        if (cells == null || cells.Length == 0)
            return Array.Empty<byte>();

        int count = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i].HasValue)
                count++;
        }

        var bytes = new byte[count];
        int index = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i].HasValue)
                bytes[index++] = cells[i].Value;
        }

        return bytes;
    }

    public static List<HexSearchMatch> FindAll(byte[] haystack, byte[] pattern)
    {
        var matches = new List<HexSearchMatch>();
        if (haystack == null || pattern == null || pattern.Length == 0 || haystack.Length < pattern.Length)
            return matches;

        for (int i = 0; i <= haystack.Length - pattern.Length; i++)
        {
            if (MatchesAt(haystack, i, pattern))
                matches.Add(new HexSearchMatch(i, pattern.Length));
        }

        return matches;
    }

    public static List<HexSearchMatch> FindAll(HexDiffPresentation presentation, HexSearchPane pane, byte[] pattern)
    {
        if (presentation == null || pattern == null || pattern.Length == 0)
            return new List<HexSearchMatch>();

        DiffByteCell[] cells = pane == HexSearchPane.PaneA
            ? presentation.LeftCells
            : presentation.RightCells;

        byte[] haystack = ExtractPaneBytes(cells);
        return FindAll(haystack, pattern);
    }

    public static int FileOffsetToCellIndex(DiffByteCell[] cells, int fileOffset)
    {
        if (cells == null || fileOffset < 0)
            return -1;

        int currentOffset = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            if (!cells[i].HasValue)
                continue;

            if (currentOffset == fileOffset)
                return i;

            currentOffset++;
        }

        return -1;
    }

    public static bool TryMapMatchToCells(
        HexDiffPresentation presentation,
        HexSearchPane pane,
        HexSearchMatch match,
        out int startCellIndex,
        out int endCellIndex)
    {
        startCellIndex = -1;
        endCellIndex = -1;

        if (presentation == null || match.Length <= 0)
            return false;

        DiffByteCell[] cells = pane == HexSearchPane.PaneA
            ? presentation.LeftCells
            : presentation.RightCells;

        startCellIndex = FileOffsetToCellIndex(cells, match.FileOffset);
        if (startCellIndex < 0)
            return false;

        int endOffset = match.FileOffset + match.Length - 1;
        int lastCellIndex = FileOffsetToCellIndex(cells, endOffset);
        if (lastCellIndex < 0)
            return false;

        endCellIndex = lastCellIndex + 1;
        return true;
    }

    public static int CellIndexToVisualRow(int cellIndex) => cellIndex / 16;

    private static bool MatchesAt(byte[] haystack, int start, byte[] pattern)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            if (haystack[start + i] != pattern[i])
                return false;
        }

        return true;
    }

    private static byte[] EncodeUtf16Le(string text)
    {
        var bytes = new byte[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bytes[i * 2] = (byte)c;
            bytes[i * 2 + 1] = (byte)(c >> 8);
        }

        return bytes;
    }

    private static bool TryParseByteSequence(string query, out byte[] pattern, out string error)
    {
        pattern = null;
        error = null;

        string trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            error = "Enter a byte sequence.";
            return false;
        }

        var bytes = new List<byte>();
        int index = 0;
        while (index < trimmed.Length)
        {
            while (index < trimmed.Length && IsSeparator(trimmed[index]))
                index++;

            if (index >= trimmed.Length)
                break;

            int start = index;
            while (index < trimmed.Length && !IsSeparator(trimmed[index]))
                index++;

            string token = trimmed.Substring(start, index - start);
            if (token.Length == 0)
                continue;

            if (token.Length > 2 || !TryParseHexByte(token, out byte value))
            {
                error = $"Invalid hex byte '{token}'. Use pairs like 05 00 00 00.";
                return false;
            }

            bytes.Add(value);
        }

        if (bytes.Count == 0)
        {
            error = "Enter at least one hex byte.";
            return false;
        }

        pattern = bytes.ToArray();
        return true;
    }

    private static bool IsSeparator(char c) =>
        c == ' ' || c == ',' || c == ';' || c == '-' || c == '\t';

    private static bool TryParseHexByte(string token, out byte value)
    {
        value = 0;
        if (token.Length == 0 || token.Length > 2)
            return false;

        for (int i = 0; i < token.Length; i++)
        {
            char c = token[i];
            int digit;
            if (c >= '0' && c <= '9')
                digit = c - '0';
            else if (c >= 'a' && c <= 'f')
                digit = c - 'a' + 10;
            else if (c >= 'A' && c <= 'F')
                digit = c - 'A' + 10;
            else
                return false;

            value = (byte)((value << 4) | digit);
        }

        return true;
    }
}
