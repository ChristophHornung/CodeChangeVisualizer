namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Describes the kind of block-level edit between two analyses.
/// </summary>
public enum DiffOpType
{
    /// <summary>An existing block changed its size in lines.</summary>
    Resize,
    /// <summary>A new block was inserted.</summary>
    Insert,
    /// <summary>An existing block was removed.</summary>
    Remove
}

/// <summary>
/// Represents a single block-level edit operation between two file analyses.
/// </summary>
public record DiffEdit
{
    /// <summary>
    /// Gets the kind of edit operation.
    /// </summary>
    public DiffOpType Kind { get; init; }

    /// <summary>
    /// The index interpretation depends on <see cref="Kind"/>:
    /// Insert/Resize use the index in the NEW sequence; Remove uses the index in the OLD sequence.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// The line type of the affected block. A block never changes its type; type changes are represented as remove+insert.
    /// </summary>
    public LineType LineType { get; init; }

    /// <summary>
    /// For Resize/Remove, the original length (lines). For Insert, null.
    /// </summary>
    public int? OldLength { get; init; }
    /// <summary>
    /// For Resize/Insert, the new length (lines). For Remove, null.
    /// </summary>
    public int? NewLength { get; init; }

    /// <summary>
    /// Convenience difference: NewLength - OldLength (missing values treated as 0).
    /// </summary>
    public int Delta => (NewLength ?? 0) - (OldLength ?? 0);
}

/// <summary>
/// Computes a minimal deterministic set of block-level edits between two file analyses.
/// </summary>
public static class Differ
{
    /// <summary>
    /// Computes block edits to transform <paramref name="oldFile"/> into <paramref name="newFile"/>.
    /// </summary>
    /// <param name="oldFile">The source analysis.</param>
    /// <param name="newFile">The target analysis.</param>
    /// <returns>A list of <see cref="DiffEdit"/> operations.</returns>
    public static List<DiffEdit> Diff(FileAnalysis oldFile, FileAnalysis newFile)
    {
        List<LineGroup> a = oldFile.Lines;
        List<LineGroup> b = newFile.Lines;
        List<DiffEdit> edits = new();

        int i = 0; // index in old (a)
        int j = 0; // index in new (b)

        while (i < a.Count && j < b.Count)
        {
            LineGroup ga = a[i];
            LineGroup gb = b[j];

            if (ga.Type == gb.Type)
            {
                if (ga.Length != gb.Length)
                {
                    edits.Add(new DiffEdit
                    {
                        Kind = DiffOpType.Resize,
                        Index = j,
                        LineType = gb.Type,
                        OldLength = ga.Length,
                        NewLength = gb.Length
                    });
                }
                i++;
                j++;
                continue;
            }

            // Types differ: try simple lookahead to decide between remove or insert to better align by type
            bool canSkipA = (i + 1 < a.Count) && a[i + 1].Type == gb.Type;
            bool canSkipB = (j + 1 < b.Count) && b[j + 1].Type == ga.Type;

            if (canSkipA && !canSkipB)
            {
                // Prefer removing from old to align
                edits.Add(new DiffEdit
                {
                    Kind = DiffOpType.Remove,
                    Index = i, // index in old sequence
                    LineType = ga.Type,
                    OldLength = ga.Length,
                    NewLength = null
                });
                i++;
            }
            else if (!canSkipA && canSkipB)
            {
                // Prefer inserting from new to align
                edits.Add(new DiffEdit
                {
                    Kind = DiffOpType.Insert,
                    Index = j, // index in new sequence where inserted
                    LineType = gb.Type,
                    OldLength = null,
                    NewLength = gb.Length
                });
                j++;
            }
            else if (canSkipA && canSkipB)
            {
                // Both possible: choose the smaller length change heuristic (or default to remove first)
                // Here we choose remove first to progress deterministically
                edits.Add(new DiffEdit
                {
                    Kind = DiffOpType.Remove,
                    Index = i,
                    LineType = ga.Type,
                    OldLength = ga.Length,
                    NewLength = null
                });
                i++;
            }
            else
            {
                // Neither aligns next; remove old first as a default choice
                edits.Add(new DiffEdit
                {
                    Kind = DiffOpType.Remove,
                    Index = i,
                    LineType = ga.Type,
                    OldLength = ga.Length,
                    NewLength = null
                });
                i++;
            }
        }

        // Remaining removes
        while (i < a.Count)
        {
            LineGroup ga = a[i];
            edits.Add(new DiffEdit
            {
                Kind = DiffOpType.Remove,
                Index = i,
                LineType = ga.Type,
                OldLength = ga.Length,
                NewLength = null
            });
            i++;
        }

        // Remaining inserts
        while (j < b.Count)
        {
            LineGroup gb = b[j];
            edits.Add(new DiffEdit
            {
                Kind = DiffOpType.Insert,
                Index = j,
                LineType = gb.Type,
                OldLength = null,
                NewLength = gb.Length
            });
            j++;
        }

        return edits;
    }
}
