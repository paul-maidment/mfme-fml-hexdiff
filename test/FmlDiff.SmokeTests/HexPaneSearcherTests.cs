using System.Collections.Generic;
using FmlDiff.Services;
using Xunit;

namespace FmlDiff.SmokeTests;

public sealed class HexPaneSearcherTests
{
    [Fact]
    public void FindAll_finds_utf8_string_in_pane_bytes()
    {
        byte[] raw = System.Text.Encoding.UTF8.GetBytes("hello world");
        var presentation = HexDiffPresentation.BuildIdentical(raw);

        Assert.True(HexPaneSearcher.TryBuildPattern("world", HexSearchMode.Utf8, out byte[] pattern, out _));
        List<HexSearchMatch> matches = HexPaneSearcher.FindAll(presentation, HexSearchPane.PaneA, pattern);

        Assert.Single(matches);
        Assert.Equal(6, matches[0].FileOffset);
        Assert.Equal(5, matches[0].Length);
    }

    [Fact]
    public void FindAll_finds_utf16le_string_in_pane_bytes()
    {
        byte[] raw = { 0x48, 0x00, 0x69, 0x00, 0x21, 0x00 };
        var presentation = HexDiffPresentation.BuildIdentical(raw);

        Assert.True(HexPaneSearcher.TryBuildPattern("Hi!", HexSearchMode.Utf16Le, out byte[] pattern, out _));
        List<HexSearchMatch> matches = HexPaneSearcher.FindAll(presentation, HexSearchPane.PaneA, pattern);

        Assert.Single(matches);
        Assert.Equal(0, matches[0].FileOffset);
        Assert.Equal(6, matches[0].Length);
    }

    [Fact]
    public void TryBuildPattern_parses_spaced_byte_sequence()
    {
        Assert.True(HexPaneSearcher.TryBuildPattern("05 00 00 00", HexSearchMode.ByteSequence, out byte[] pattern, out _));
        Assert.Equal(new byte[] { 0x05, 0x00, 0x00, 0x00 }, pattern);
    }

    [Fact]
    public void FindAll_finds_byte_sequence()
    {
        byte[] raw = { 0x01, 0x05, 0x00, 0x00, 0x00, 0x02 };
        var presentation = HexDiffPresentation.BuildIdentical(raw);

        Assert.True(HexPaneSearcher.TryBuildPattern("05 00 00 00", HexSearchMode.ByteSequence, out byte[] pattern, out _));
        List<HexSearchMatch> matches = HexPaneSearcher.FindAll(presentation, HexSearchPane.PaneA, pattern);

        Assert.Single(matches);
        Assert.Equal(1, matches[0].FileOffset);
    }

    [Fact]
    public void TryMapMatchToCells_maps_file_offset_to_cell_range()
    {
        byte[] raw = { 0x01, 0x02, 0x03, 0x04 };
        var presentation = HexDiffPresentation.BuildIdentical(raw);
        var match = new HexSearchMatch(1, 2);

        Assert.True(HexPaneSearcher.TryMapMatchToCells(presentation, HexSearchPane.PaneA, match, out int start, out int end));
        Assert.Equal(1, start);
        Assert.Equal(3, end);
    }

    [Fact]
    public void TryBuildPattern_rejects_invalid_hex_byte()
    {
        Assert.False(HexPaneSearcher.TryBuildPattern("0G", HexSearchMode.ByteSequence, out _, out string error));
        Assert.Contains("Invalid hex byte", error);
    }
}
