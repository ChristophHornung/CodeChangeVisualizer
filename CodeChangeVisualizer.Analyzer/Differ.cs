namespace CodeChangeVisualizer.Analyzer;

public enum DiffOpType
{
    Resize,
    Insert,
    Remove
}

public record DiffEdit
{
    public DiffOpType Kind { get; init; }

    // Index meaning by operation:
    // - Insert: index in the NEW sequence where the block is inserted
    // - Remove: index in the OLD sequence of the block being removed
    // - Resize: index in the NEW sequence of the aligned block whose size changed
    public int Index { get; init; }

    public LineType LineType { get; init; }

    // For Resize: OldLength and NewLength are set; Delta = NewLength - OldLength
    // For Insert: NewLength is set; OldLength is null; Delta = NewLength
    // For Remove: OldLength is set; NewLength is null; Delta = -OldLength
    public int? OldLength { get; init; }
    public int? NewLength { get; init; }

    public int Delta => (NewLength ?? 0) - (OldLength ?? 0);
}

public static class Differ
{
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
