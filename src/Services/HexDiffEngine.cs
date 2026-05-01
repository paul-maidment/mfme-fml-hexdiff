using System;
using System.Collections.Generic;

namespace FmlDiff.Services;

public enum ByteCellKind
{
    Normal,
    Gap,
    Inserted,
    Empty
}

public readonly record struct DiffByteCell(ByteCellKind Kind, byte Value)
{
    public bool HasValue => Kind == ByteCellKind.Normal || Kind == ByteCellKind.Inserted;

    public static DiffByteCell Normal(byte value) => new(ByteCellKind.Normal, value);
    public static DiffByteCell Inserted(byte value) => new(ByteCellKind.Inserted, value);
    public static DiffByteCell Gap() => new(ByteCellKind.Gap, 0);
    public static DiffByteCell Empty() => new(ByteCellKind.Empty, 0);
}

public sealed record InsertionRecord(int OffsetA, int OffsetB, byte[] Bytes);

public sealed record DiffLayoutResult(
    IReadOnlyList<DiffByteCell> LeftCells,
    IReadOnlyList<DiffByteCell> RightCells,
    IReadOnlyList<InsertionRecord> Insertions,
    int UnresolvedMismatchCount
);

public sealed class HexDiffEngine
{
    public DiffLayoutResult BuildAligned(IReadOnlyList<byte> leftBytes, IReadOnlyList<byte> rightBytes, int lookaheadWindow)
    {
        if (leftBytes == null) throw new ArgumentNullException(nameof(leftBytes));
        if (rightBytes == null) throw new ArgumentNullException(nameof(rightBytes));
        if (lookaheadWindow < 0) throw new ArgumentOutOfRangeException(nameof(lookaheadWindow));

        List<DiffByteCell> leftCells = new();
        List<DiffByteCell> rightCells = new();
        List<InsertionRecord> insertions = new();

        int leftIndex = 0;
        int rightIndex = 0;
        int unresolvedMismatches = 0;

        while (leftIndex < leftBytes.Count || rightIndex < rightBytes.Count)
        {
            if (leftIndex >= leftBytes.Count)
            {
                int start = rightIndex;
                int remaining = rightBytes.Count - rightIndex;
                byte[] insertedTail = new byte[remaining];
                for (int i = 0; i < remaining; i++)
                {
                    byte b = rightBytes[rightIndex++];
                    insertedTail[i] = b;
                    leftCells.Add(DiffByteCell.Gap());
                    rightCells.Add(DiffByteCell.Inserted(b));
                }

                if (remaining > 0)
                {
                    insertions.Add(new InsertionRecord(leftIndex, start, insertedTail));
                }

                continue;
            }

            if (rightIndex >= rightBytes.Count)
            {
                byte a = leftBytes[leftIndex++];
                leftCells.Add(DiffByteCell.Normal(a));
                rightCells.Add(DiffByteCell.Gap());
                continue;
            }

            byte left = leftBytes[leftIndex];
            byte right = rightBytes[rightIndex];
            if (left == right)
            {
                leftCells.Add(DiffByteCell.Normal(left));
                rightCells.Add(DiffByteCell.Normal(right));
                leftIndex++;
                rightIndex++;
                continue;
            }

            // Search right stream for current left byte (bytes inserted into right)
            int resyncRight = FindResyncDistance(rightBytes, rightIndex, left, lookaheadWindow);
            // Search left stream for current right byte (bytes inserted into left / deleted from right)
            int resyncLeft = FindResyncDistance(leftBytes, leftIndex, right, lookaheadWindow);

            bool canRight = resyncRight > 0;
            bool canLeft = resyncLeft > 0;

            if (canRight && (!canLeft || resyncRight <= resyncLeft))
            {
                // Gap(s) on left, advance right — bytes were inserted in the right file
                byte[] insertedBytes = new byte[resyncRight];
                int startB = rightIndex;
                for (int i = 0; i < resyncRight; i++)
                {
                    byte inserted = rightBytes[rightIndex++];
                    insertedBytes[i] = inserted;
                    leftCells.Add(DiffByteCell.Gap());
                    rightCells.Add(DiffByteCell.Inserted(inserted));
                }
                insertions.Add(new InsertionRecord(leftIndex, startB, insertedBytes));
            }
            else if (canLeft)
            {
                // Gap(s) on right, advance left — bytes were deleted from the right file
                for (int i = 0; i < resyncLeft; i++)
                {
                    byte deleted = leftBytes[leftIndex++];
                    leftCells.Add(DiffByteCell.Normal(deleted));
                    rightCells.Add(DiffByteCell.Gap());
                }
            }
            else
            {
                unresolvedMismatches++;
                leftCells.Add(DiffByteCell.Normal(leftBytes[leftIndex++]));
                rightCells.Add(DiffByteCell.Normal(rightBytes[rightIndex++]));
            }
        }

        return new DiffLayoutResult(leftCells, rightCells, insertions, unresolvedMismatches);
    }

    public DiffLayoutResult BuildRaw(IReadOnlyList<byte> leftBytes, IReadOnlyList<byte> rightBytes)
    {
        if (leftBytes == null) throw new ArgumentNullException(nameof(leftBytes));
        if (rightBytes == null) throw new ArgumentNullException(nameof(rightBytes));

        List<DiffByteCell> leftCells = new();
        List<DiffByteCell> rightCells = new();
        int length = Math.Max(leftBytes.Count, rightBytes.Count);

        for (int i = 0; i < length; i++)
        {
            leftCells.Add(i < leftBytes.Count ? DiffByteCell.Normal(leftBytes[i]) : DiffByteCell.Empty());
            rightCells.Add(i < rightBytes.Count ? DiffByteCell.Normal(rightBytes[i]) : DiffByteCell.Empty());
        }

        return new DiffLayoutResult(leftCells, rightCells, Array.Empty<InsertionRecord>(), 0);
    }

    private static int FindResyncDistance(
        IReadOnlyList<byte> searchIn,
        int searchFrom,
        byte target,
        int lookaheadWindow)
    {
        if (lookaheadWindow <= 0)
            return -1;

        int maxDistance = Math.Min(lookaheadWindow, searchIn.Count - searchFrom - 1);
        for (int distance = 1; distance <= maxDistance; distance++)
        {
            if (searchIn[searchFrom + distance] == target)
                return distance;
        }

        return -1;
    }
}
