namespace CodeChangeVisualizer.Tests;

using CodeChangeVisualizer.Analyzer;
using DiffApplier = CodeChangeVisualizer.Analyzer.FileAnalysisApplier;

public class DiffApplierTests
{
	private static LineGroup Lg(LineType type, int length, int start = 0) =>
		new LineGroup { Type = type, Length = length, Start = start };

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
		foreach (LineGroup g in actual.Lines)
		{
			Assert.Equal(start, g.Start);
			start += g.Length;
		}
	}

	[Fact]
	public void Apply_KnownEdits_WithMultipleAdjacentInsertsAndRemoves()
	{
		FileAnalysis oldFa = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup>
			{
				DiffApplierTests.Lg(LineType.Code, 3), // 0
				DiffApplierTests.Lg(LineType.Comment, 2), // 1
				DiffApplierTests.Lg(LineType.Code, 5), // 2
			}
		};

		// We want new: [Insert Empty(1)] , Code(3) resized->4 , [Remove Comment(2)] , Code(5), Insert Comment(1)
		List<DiffEdit> edits = new List<DiffEdit>
		{
			new DiffEdit { Kind = DiffOpType.Insert, Index = 0, LineType = LineType.Empty, NewLength = 1 },
			new DiffEdit
				{ Kind = DiffOpType.Resize, Index = 1, LineType = LineType.Code, OldLength = 3, NewLength = 4 },
			new DiffEdit { Kind = DiffOpType.Remove, Index = 1, LineType = LineType.Comment, OldLength = 2 },
			new DiffEdit { Kind = DiffOpType.Insert, Index = 3, LineType = LineType.Comment, NewLength = 1 },
		};

		FileAnalysis patched = DiffApplier.Apply(oldFa, edits);

		FileAnalysis expected = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup>
			{
				DiffApplierTests.Lg(LineType.Empty, 1),
				DiffApplierTests.Lg(LineType.Code, 4),
				DiffApplierTests.Lg(LineType.Code, 5),
				DiffApplierTests.Lg(LineType.Comment, 1)
			}
		};

		DiffApplierTests.AssertSameSequence(expected, patched);
	}

	[Fact]
	public void Roundtrip_ComplexMixed()
	{
		FileAnalysis oldFa = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup>
			{
				DiffApplierTests.Lg(LineType.Code, 5),
				DiffApplierTests.Lg(LineType.Comment, 2),
				DiffApplierTests.Lg(LineType.CodeAndComment, 4)
			}
		};
		FileAnalysis newFa = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup>
			{
				DiffApplierTests.Lg(LineType.Code, 7),
				DiffApplierTests.Lg(LineType.CodeAndComment, 4),
				DiffApplierTests.Lg(LineType.Empty, 1),
			}
		};

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		FileAnalysis patched = DiffApplier.Apply(oldFa, edits);

		DiffApplierTests.AssertSameSequence(newFa, patched);
	}

	[Fact]
	public void Roundtrip_DeletedFile_ToEmpty()
	{
		FileAnalysis oldFa = new FileAnalysis
		{
			File = "a.cs", Lines = new List<LineGroup>
			{
				new LineGroup { Type = LineType.Code, Length = 5 },
				new LineGroup { Type = LineType.Comment, Length = 2 },
			}
		};
		FileAnalysis newFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup>() };

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		FileAnalysis patched = DiffApplier.Apply(oldFa, edits);

		Assert.Equal(2, edits.Count);
		Assert.All(edits, e => Assert.Equal(DiffOpType.Remove, e.Kind));
		Assert.Empty(patched.Lines);
	}

	[Fact]
	public void Roundtrip_Insert()
	{
		FileAnalysis oldFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { DiffApplierTests.Lg(LineType.Code, 10) } };
		FileAnalysis newFa = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup>
				{ DiffApplierTests.Lg(LineType.Code, 10), DiffApplierTests.Lg(LineType.Comment, 3) }
		};

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		FileAnalysis patched = DiffApplier.Apply(oldFa, edits);

		DiffApplierTests.AssertSameSequence(newFa, patched);
	}

	[Fact]
	public void Roundtrip_NewFile_FromEmpty()
	{
		FileAnalysis oldFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup>() };
		FileAnalysis newFa = new FileAnalysis
		{
			File = "a.cs", Lines = new List<LineGroup>
			{
				new LineGroup { Type = LineType.Code, Length = 5 },
				new LineGroup { Type = LineType.Comment, Length = 2 },
			}
		};

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		FileAnalysis patched = DiffApplier.Apply(oldFa, edits);

		Assert.Equal(2, edits.Count);
		Assert.All(edits, e => Assert.Equal(DiffOpType.Insert, e.Kind));
		// Verify result equals new file
		Assert.Equal(newFa.Lines.Count, patched.Lines.Count);
		for (int i = 0; i < newFa.Lines.Count; i++)
		{
			Assert.Equal(newFa.Lines[i].Type, patched.Lines[i].Type);
			Assert.Equal(newFa.Lines[i].Length, patched.Lines[i].Length);
		}
	}

	[Fact]
	public void Roundtrip_NoChanges()
	{
		FileAnalysis oldFa = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup>
				{ DiffApplierTests.Lg(LineType.Code, 10), DiffApplierTests.Lg(LineType.Comment, 3) }
		};
		FileAnalysis newFa = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup>
				{ DiffApplierTests.Lg(LineType.Code, 10), DiffApplierTests.Lg(LineType.Comment, 3) }
		};

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		FileAnalysis patched = DiffApplier.Apply(oldFa, edits);

		DiffApplierTests.AssertSameSequence(newFa, patched);
	}

	[Fact]
	public void Roundtrip_Remove()
	{
		FileAnalysis oldFa = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup>
				{ DiffApplierTests.Lg(LineType.Code, 10), DiffApplierTests.Lg(LineType.Comment, 3) }
		};
		FileAnalysis newFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { DiffApplierTests.Lg(LineType.Code, 10) } };

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		FileAnalysis patched = DiffApplier.Apply(oldFa, edits);

		DiffApplierTests.AssertSameSequence(newFa, patched);
	}

	[Fact]
	public void Roundtrip_ResizeOnly()
	{
		FileAnalysis oldFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { DiffApplierTests.Lg(LineType.Code, 10) } };
		FileAnalysis newFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { DiffApplierTests.Lg(LineType.Code, 12) } };

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		FileAnalysis patched = DiffApplier.Apply(oldFa, edits);

		DiffApplierTests.AssertSameSequence(newFa, patched);
	}

	[Fact]
	public void Roundtrip_TypeChange_AsRemoveInsert()
	{
		FileAnalysis oldFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { DiffApplierTests.Lg(LineType.Code, 10) } };
		FileAnalysis newFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { DiffApplierTests.Lg(LineType.Comment, 10) } };

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		FileAnalysis patched = DiffApplier.Apply(oldFa, edits);

		DiffApplierTests.AssertSameSequence(newFa, patched);
	}
}