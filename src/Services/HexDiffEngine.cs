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
    // Myers diff is used when the edit distance D is within this cap.
    // For D > cap the bidirectional greedy fallback is used instead.
    // Memory usage scales O(D²): at cap=2000 the trace takes ~8 MB.
    private const int MaxMyersEditDistance = 2000;

    public DiffLayoutResult BuildAligned(IReadOnlyList<byte> leftBytes, IReadOnlyList<byte> rightBytes, int lookaheadWindow)
    {
        if (leftBytes == null) throw new ArgumentNullException(nameof(leftBytes));
        if (rightBytes == null) throw new ArgumentNullException(nameof(rightBytes));
        if (lookaheadWindow < 0) throw new ArgumentOutOfRangeException(nameof(lookaheadWindow));

        List<DiffByteCell> leftCells;
        List<DiffByteCell> rightCells;
        List<InsertionRecord> insertions = new();
        int unresolvedMismatches = 0;

        bool myersSucceeded = TryMyersDiff(leftBytes, rightBytes, out List<EditOp> editScript);

        if (myersSucceeded)
        {
            leftCells = new List<DiffByteCell>(editScript.Count);
            rightCells = new List<DiffByteCell>(editScript.Count);
            BuildCellsFromEditScript(editScript, leftCells, rightCells, insertions);
        }
        else
        {
            // Edit distance exceeded MaxMyersEditDistance — fall back to bidirectional greedy.
            leftCells = new List<DiffByteCell>(leftBytes.Count + rightBytes.Count);
            rightCells = new List<DiffByteCell>(leftBytes.Count + rightBytes.Count);
            BuildCellsGreedy(leftBytes, rightBytes, lookaheadWindow,
                leftCells, rightCells, insertions, out unresolvedMismatches);
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

    // -------------------------------------------------------------------------
    // Myers diff — O((N+M)*D) time, O(D²) space via compact trace storage.
    //
    // Returns false (out editScript = null) when D > MaxMyersEditDistance; caller falls back to greedy.
    //
    // Algorithm overview:
    //   Walks both sequences on a grid where moving RIGHT = Delete (byte only in
    //   left) and moving DOWN = Insert (byte only in right). Diagonal k = x - y.
    //   Each "edit step" d extends the furthest-reaching point on each diagonal
    //   reachable with exactly d edits, using matching bytes (a "snake") for free.
    //   The trace (compact array of furthest-x per diagonal, one snapshot per
    //   step) lets us backtrack the optimal path to produce the edit script.
    // -------------------------------------------------------------------------
    private static bool TryMyersDiff(IReadOnlyList<byte> a, IReadOnlyList<byte> b, out List<EditOp> result)
    {
        int n = a.Count, m = b.Count;

        if (n == 0)
        {
            var ops = new List<EditOp>(m);
            for (int i = 0; i < m; i++) ops.Add(new EditOp(EditOpKind.Insert, b[i]));
            result = ops;
            return true;
        }
        if (m == 0)
        {
            var ops = new List<EditOp>(n);
            for (int i = 0; i < n; i++) ops.Add(new EditOp(EditOpKind.Delete, a[i]));
            result = ops;
            return true;
        }

        int dMax = Math.Min(MaxMyersEditDistance, n + m);

        // v[k + vOffset] = furthest x on diagonal k.
        // We need k in [-(dMax+1), dMax+1] for the boundary sentinel reads.
        int vOffset = dMax + 1;
        int[] v = new int[2 * (dMax + 2) + 1];
        v[1 + vOffset] = 0; // standard Myers sentinel: diagonal k=1 starts at x=0

        // Compact trace: trace[d] is an int[] of size (d+1) storing V[k] for
        // k = -d, -d+2, ..., d (one entry per reachable diagonal at step d).
        // Mapping:  trace[d][(k + d) / 2]  =  V[k]
        // Total entries across all steps: 1+2+...+(D+1) = O(D²).
        var trace = new int[dMax + 1][];

        int foundD = -1;

        for (int d = 0; d <= dMax; d++)
        {
            for (int k = -d; k <= d; k += 2)
            {
                // Decide whether to move down (Insert) or right (Delete).
                bool moveDown = (k == -d) || (k != d && v[k - 1 + vOffset] < v[k + 1 + vOffset]);
                int x = moveDown ? v[k + 1 + vOffset] : v[k - 1 + vOffset] + 1;
                int y = x - k;

                // Extend the "snake" — free diagonal moves for matching bytes.
                while (x < n && y < m && a[x] == b[y]) { x++; y++; }

                v[k + vOffset] = x;

                if (x >= n && y >= m)
                {
                    foundD = d;
                    goto backtrack;
                }
            }

            // Save V snapshot for diagonals reachable at this step.
            // Backtracking at step d+1 reads this as trace[d].
            var snapshot = new int[d + 1];
            for (int ki = -d; ki <= d; ki += 2)
                snapshot[(ki + d) / 2] = v[ki + vOffset];
            trace[d] = snapshot;
        }

        result = null;
        return false; // edit distance exceeded cap

        backtrack:
        result = BuildEditScript(a, b, trace, foundD, n, m);
        return true;
    }

    private static List<EditOp> BuildEditScript(
        IReadOnlyList<byte> a, IReadOnlyList<byte> b,
        int[][] trace, int foundD, int n, int m)
    {
        var script = new List<EditOp>(n + m);

        // Walk backwards from (n, m) to (0, 0), one edit step at a time.
        // At each step d, trace[d-1] holds V at the end of step d-1, telling
        // us which diagonal we were on before the current edit + snake.
        int cx = n, cy = m;

        for (int d = foundD; d > 0; d--)
        {
            int[] prevSnap = trace[d - 1];
            int k = cx - cy;

            bool moveDown;
            if (k == -d)
                moveDown = true;
            else if (k == d)
                moveDown = false;
            else
                moveDown = GetCompactV(prevSnap, d - 1, k - 1) < GetCompactV(prevSnap, d - 1, k + 1);

            int kPrev = moveDown ? k + 1 : k - 1;
            int xPrev = GetCompactV(prevSnap, d - 1, kPrev);
            int yPrev = xPrev - kPrev;

            // (xMid, yMid) = position immediately after the edit, before the snake.
            // Insert keeps x, advances y.  Delete advances x, keeps y.
            int xMid = moveDown ? xPrev : xPrev + 1;
            int yMid = xMid - k; // always equals xMid - k on the current diagonal

            // Emit the snake (cx→xMid matched bytes) in reverse order.
            for (int i = cx - 1; i >= xMid; i--)
                script.Add(new EditOp(EditOpKind.Keep, a[i]));

            // Emit the single edit that preceded the snake.
            if (moveDown)
                script.Add(new EditOp(EditOpKind.Insert, b[yMid - 1]));
            else
                script.Add(new EditOp(EditOpKind.Delete, a[xMid - 1]));

            cx = xPrev;
            cy = yPrev;
        }

        // Emit the initial common prefix (matched bytes before the first edit).
        for (int i = cx - 1; i >= 0; i--)
            script.Add(new EditOp(EditOpKind.Keep, a[i]));

        script.Reverse();
        return script;
    }

    // Read a value from a compact snapshot array.
    // compactSnap[(k + d) / 2] = V[k] for k in [-d, d] step 2.
    // Returns 0 for k outside the valid range (unset diagonal).
    private static int GetCompactV(int[] compactSnap, int d, int k)
    {
        if (compactSnap == null || Math.Abs(k) > d || ((k + d) & 1) != 0)
            return 0;
        return compactSnap[(k + d) / 2];
    }

    // -------------------------------------------------------------------------
    // Convert a Myers edit script into paired DiffByteCell lists.
    // -------------------------------------------------------------------------
    private static void BuildCellsFromEditScript(
        List<EditOp> ops,
        List<DiffByteCell> leftCells,
        List<DiffByteCell> rightCells,
        List<InsertionRecord> insertions)
    {
        int leftFileIndex = 0;
        int rightFileIndex = 0;
        int insertGroupStart = -1;
        int insertGroupStartB = -1;
        var insertBuffer = new List<byte>();

        foreach (EditOp op in ops)
        {
            switch (op.Kind)
            {
                case EditOpKind.Insert:
                    if (insertGroupStart < 0)
                    {
                        insertGroupStart = leftFileIndex;
                        insertGroupStartB = rightFileIndex;
                        insertBuffer.Clear();
                    }
                    insertBuffer.Add(op.Value);
                    leftCells.Add(DiffByteCell.Gap());
                    rightCells.Add(DiffByteCell.Inserted(op.Value));
                    rightFileIndex++;
                    break;

                default:
                    if (insertGroupStart >= 0)
                    {
                        insertions.Add(new InsertionRecord(insertGroupStart, insertGroupStartB, insertBuffer.ToArray()));
                        insertGroupStart = -1;
                    }
                    leftCells.Add(DiffByteCell.Normal(op.Value));
                    if (op.Kind == EditOpKind.Keep)
                    {
                        rightCells.Add(DiffByteCell.Normal(op.Value));
                        rightFileIndex++;
                    }
                    else // Delete
                    {
                        rightCells.Add(DiffByteCell.Gap());
                    }
                    leftFileIndex++;
                    break;
            }
        }

        if (insertGroupStart >= 0)
            insertions.Add(new InsertionRecord(insertGroupStart, insertGroupStartB, insertBuffer.ToArray()));
    }

    // -------------------------------------------------------------------------
    // Bidirectional greedy resync — used as fallback when D > MaxMyersEditDistance.
    // -------------------------------------------------------------------------
    private static void BuildCellsGreedy(
        IReadOnlyList<byte> leftBytes,
        IReadOnlyList<byte> rightBytes,
        int lookaheadWindow,
        List<DiffByteCell> leftCells,
        List<DiffByteCell> rightCells,
        List<InsertionRecord> insertions,
        out int unresolvedMismatches)
    {
        int leftIndex = 0, rightIndex = 0;
        unresolvedMismatches = 0;

        while (leftIndex < leftBytes.Count || rightIndex < rightBytes.Count)
        {
            if (leftIndex >= leftBytes.Count)
            {
                int start = rightIndex;
                int remaining = rightBytes.Count - rightIndex;
                byte[] tail = new byte[remaining];
                for (int i = 0; i < remaining; i++)
                {
                    byte b = rightBytes[rightIndex++];
                    tail[i] = b;
                    leftCells.Add(DiffByteCell.Gap());
                    rightCells.Add(DiffByteCell.Inserted(b));
                }
                if (remaining > 0)
                    insertions.Add(new InsertionRecord(leftIndex, start, tail));
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

            int resyncRight = FindResyncDistance(rightBytes, rightIndex, left, lookaheadWindow);
            int resyncLeft = FindResyncDistance(leftBytes, leftIndex, right, lookaheadWindow);
            bool canRight = resyncRight > 0;
            bool canLeft = resyncLeft > 0;

            if (canRight && (!canLeft || resyncRight <= resyncLeft))
            {
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

    private enum EditOpKind { Keep, Insert, Delete }
    private readonly record struct EditOp(EditOpKind Kind, byte Value);
}
