using System;
using System.Collections.Generic;

namespace FmlDiff.Services;

public sealed class HexDiffPresentation
{
    internal HexDiffPresentation(
        DiffByteCell[] leftCells,
        DiffByteCell[] rightCells,
        int[] leftRowOffsets,
        int[] rightRowOffsets,
        List<(int VisualRow, int LeftFileOffset)> diffRows)
    {
        LeftCells = leftCells;
        RightCells = rightCells;
        LeftRowOffsets = leftRowOffsets;
        RightRowOffsets = rightRowOffsets;
        DiffRows = diffRows;
        RowCount = leftRowOffsets.Length;
    }

    public DiffByteCell[] LeftCells { get; }
    public DiffByteCell[] RightCells { get; }
    public int RowCount { get; }
    public int[] LeftRowOffsets { get; }
    public int[] RightRowOffsets { get; }
    public IReadOnlyList<(int VisualRow, int LeftFileOffset)> DiffRows { get; }

    public static HexDiffPresentation Build(IReadOnlyList<DiffByteCell> leftCells, IReadOnlyList<DiffByteCell> rightCells)
    {
        if (leftCells == null) throw new ArgumentNullException(nameof(leftCells));
        if (rightCells == null) throw new ArgumentNullException(nameof(rightCells));

        const int bytesPerRow = 16;
        int totalCells = Math.Max(leftCells.Count, rightCells.Count);
        int rowCount = (totalCells + bytesPerRow - 1) / bytesPerRow;

        var leftRowOffsets = new int[rowCount];
        var rightRowOffsets = new int[rowCount];
        var diffRows = new List<(int, int)>();

        int leftFileOffset = 0;
        int rightFileOffset = 0;

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            leftRowOffsets[rowIndex] = leftFileOffset;
            rightRowOffsets[rowIndex] = rightFileOffset;
            bool rowHasDiff = false;

            for (int i = 0; i < bytesPerRow; i++)
            {
                int cellIndex = rowIndex * bytesPerRow + i;

                DiffByteCell lc = cellIndex < leftCells.Count ? leftCells[cellIndex] : DiffByteCell.Empty();
                DiffByteCell rc = cellIndex < rightCells.Count ? rightCells[cellIndex] : DiffByteCell.Empty();

                bool leftIsGap = lc.Kind is ByteCellKind.Gap or ByteCellKind.Empty;
                bool rightIsGap = rc.Kind is ByteCellKind.Gap or ByteCellKind.Empty;

                if (!leftIsGap && !rightIsGap)
                {
                    if (lc.Value != rc.Value)
                        rowHasDiff = true;
                }
                else if (leftIsGap != rightIsGap)
                {
                    rowHasDiff = true;
                }

                if (!leftIsGap)
                    leftFileOffset++;

                if (!rightIsGap)
                    rightFileOffset++;
            }

            if (rowHasDiff)
                diffRows.Add((rowIndex, leftRowOffsets[rowIndex]));
        }

        return new HexDiffPresentation(
            ToCellArray(leftCells),
            ToCellArray(rightCells),
            leftRowOffsets,
            rightRowOffsets,
            diffRows);
    }

    private static DiffByteCell[] ToCellArray(IReadOnlyList<DiffByteCell> cells)
    {
        if (cells is DiffByteCell[] array)
            return array;

        var copy = new DiffByteCell[cells.Count];
        for (int i = 0; i < cells.Count; i++)
            copy[i] = cells[i];

        return copy;
    }
}
