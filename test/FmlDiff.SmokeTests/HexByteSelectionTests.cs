using FmlDiff.Services;
using Xunit;

namespace FmlDiff.SmokeTests;

public sealed class HexByteSelectionTests
{
    [Fact]
    public void ExtractBytesFromCellRange_returns_bytes_in_order_skipping_gaps()
    {
        var cells = new[]
        {
            DiffByteCell.Normal(0x01),
            DiffByteCell.Gap(),
            DiffByteCell.Normal(0x03),
            DiffByteCell.Normal(0x04)
        };

        byte[] bytes = HexByteSelection.ExtractBytesFromCellRange(cells, 0, 3);

        Assert.Equal(new byte[] { 0x01, 0x03, 0x04 }, bytes);
    }

    [Fact]
    public void FormatBytesAsHex_uses_spaced_uppercase_pairs()
    {
        Assert.Equal("05 00 00 00", HexByteSelection.FormatBytesAsHex(new byte[] { 0x05, 0x00, 0x00, 0x00 }));
    }
}
