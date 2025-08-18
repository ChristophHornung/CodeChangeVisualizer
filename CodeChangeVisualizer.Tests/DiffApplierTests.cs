namespace CodeChangeVisualizer.Tests;

using CodeChangeVisualizer.Analyzer;

public class DiffApplierTests
{
    private static LineGroup LG(LineType type, int length, int start = 0) => new LineGroup { Type = type, Length = length, Start = start };

    private static void AssertSameSequence(FileAnalysis expected, FileAnalysis actual)
    {
        Assert.Equal(expected.Lines.Count, actual.Lines.Count);
        for (int i = 0; i < expected.Lines.Count; i++)
        {
            Assert.Equal(expected.Lines[i].Type, actual.Lines[i].Type);
            Assert.Equal(expected.Lines[i].Length, actual.Lines[i].Length);
        }

        // Also validate Start fields are contiguous starting at 0
        int start = 0;
        foreach (var g in actual.Lines)
        {
            Assert.Equal(start, g.Start);
            start += g.Length;
        }
    }

    [Fact]
    public void Roundtrip_NoChanges()
    {
        var oldFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup> { LG(LineType.Code, 10), LG(LineType.Comment, 3) } };
        var newFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup> { LG(LineType.Code, 10), LG(LineType.Comment, 3) } };

        var edits = Differ.Diff(oldFa, newFa);
        var patched = DiffApplier.Apply(oldFa, edits);

        AssertSameSequence(newFa, patched);
    }

    [Fact]
    public void Roundtrip_ResizeOnly()
    {
        var oldFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup> { LG(LineType.Code, 10) } };
        var newFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup> { LG(LineType.Code, 12) } };

        var edits = Differ.Diff(oldFa, newFa);
        var patched = DiffApplier.Apply(oldFa, edits);

        AssertSameSequence(newFa, patched);
    }

    [Fact]
    public void Roundtrip_Insert()
    {
        var oldFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup> { LG(LineType.Code, 10) } };
        var newFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup> { LG(LineType.Code, 10), LG(LineType.Comment, 3) } };

        var edits = Differ.Diff(oldFa, newFa);
        var patched = DiffApplier.Apply(oldFa, edits);

        AssertSameSequence(newFa, patched);
    }

    [Fact]
    public void Roundtrip_Remove()
    {
        var oldFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup> { LG(LineType.Code, 10), LG(LineType.Comment, 3) } };
        var newFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup> { LG(LineType.Code, 10) } };

        var edits = Differ.Diff(oldFa, newFa);
        var patched = DiffApplier.Apply(oldFa, edits);

        AssertSameSequence(newFa, patched);
    }

    [Fact]
    public void Roundtrip_TypeChange_AsRemoveInsert()
    {
        var oldFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup> { LG(LineType.Code, 10) } };
        var newFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup> { LG(LineType.Comment, 10) } };

        var edits = Differ.Diff(oldFa, newFa);
        var patched = DiffApplier.Apply(oldFa, edits);

        AssertSameSequence(newFa, patched);
    }

    [Fact]
    public void Roundtrip_ComplexMixed()
    {
        var oldFa = new FileAnalysis
        {
            File = "a.cs",
            Lines = new List<LineGroup>
            {
                LG(LineType.Code, 5),
                LG(LineType.Comment, 2),
                LG(LineType.CodeAndComment, 4)
            }
        };
        var newFa = new FileAnalysis
        {
            File = "a.cs",
            Lines = new List<LineGroup>
            {
                LG(LineType.Code, 7),
                LG(LineType.CodeAndComment, 4),
                LG(LineType.Empty, 1),
            }
        };

        var edits = Differ.Diff(oldFa, newFa);
        var patched = DiffApplier.Apply(oldFa, edits);

        AssertSameSequence(newFa, patched);
    }

    [Fact]
    public void Apply_KnownEdits_WithMultipleAdjacentInsertsAndRemoves()
    {
        var oldFa = new FileAnalysis
        {
            File = "a.cs",
            Lines = new List<LineGroup>
            {
                LG(LineType.Code, 3),   // 0
                LG(LineType.Comment, 2),// 1
                LG(LineType.Code, 5),   // 2
            }
        };

        // We want new: [Insert Empty(1)] , Code(3) resized->4 , [Remove Comment(2)] , Code(5), Insert Comment(1)
        var edits = new List<DiffEdit>
        {
            new DiffEdit { Kind = DiffOpType.Insert, Index = 0, LineType = LineType.Empty, NewLength = 1 },
            new DiffEdit { Kind = DiffOpType.Resize, Index = 1, LineType = LineType.Code, OldLength = 3, NewLength = 4 },
            new DiffEdit { Kind = DiffOpType.Remove, Index = 1, LineType = LineType.Comment, OldLength = 2 },
            new DiffEdit { Kind = DiffOpType.Insert, Index = 3, LineType = LineType.Comment, NewLength = 1 },
        };

        var patched = DiffApplier.Apply(oldFa, edits);

        var expected = new FileAnalysis
        {
            File = "a.cs",
            Lines = new List<LineGroup>
            {
                LG(LineType.Empty, 1),
                LG(LineType.Code, 4),
                LG(LineType.Code, 5),
                LG(LineType.Comment, 1)
            }
        };

        AssertSameSequence(expected, patched);
    }
}
