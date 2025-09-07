namespace CodeChangeVisualizer.Tests;

using CodeChangeVisualizer.Analyzer;

public class DifferTests
{
	private static LineGroup Lg(LineType type, int length, int start = 0) =>
		new LineGroup { Type = type, Length = length, Start = start };

	[Fact]
	public void Complex_MixedOperations_AreDetectedInOrder()
	{
		FileAnalysis oldFa = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup>
			{
				DifferTests.Lg(LineType.Code, 5),
				DifferTests.Lg(LineType.Comment, 2),
				DifferTests.Lg(LineType.CodeAndComment, 4)
			}
		};
		FileAnalysis newFa = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup>
			{
				DifferTests.Lg(LineType.Code, 7), // resized from 5 -> 7
				DifferTests.Lg(LineType.CodeAndComment, 4), // comment removed, CC aligned
				DifferTests.Lg(LineType.Empty, 1), // new empty insert
			}
		};

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);

		// Expect: Resize(Code idx0), Remove(Comment old idx1), (CodeAndComment same no op), Insert(Empty idx2)
		Assert.Equal(3, edits.Count);

		Assert.Equal(DiffOpType.Resize, edits[0].Kind);
		Assert.Equal(0, edits[0].Index);
		Assert.Equal(LineType.Code, edits[0].LineType);
		Assert.Equal(5, edits[0].OldLength);
		Assert.Equal(7, edits[0].NewLength);

		Assert.Equal(DiffOpType.Remove, edits[1].Kind);
		Assert.Equal(1, edits[1].Index); // old index
		Assert.Equal(LineType.Comment, edits[1].LineType);

		Assert.Equal(DiffOpType.Insert, edits[2].Kind);
		Assert.Equal(2, edits[2].Index); // new index
		Assert.Equal(LineType.Empty, edits[2].LineType);
		Assert.Equal(1, edits[2].NewLength);
	}

	[Fact]
	public void DeletedFile_ShouldProduceAllRemoves()
	{
		FileAnalysis oldFa = new FileAnalysis
		{
			File = "a.cs", Lines = new List<LineGroup>
			{
				new LineGroup { Type = LineType.Code, Length = 5 },
				new LineGroup { Type = LineType.Comment, Length = 2 }
			}
		};
		FileAnalysis newFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup>() };

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		Assert.Equal(2, edits.Count);
		Assert.All(edits, e => Assert.Equal(DiffOpType.Remove, e.Kind));
		// Order: removes from old indices 0 then 1 (greedy), or possibly 0 then 1
		Assert.Equal(0, edits[0].Index);
		Assert.Equal(LineType.Code, edits[0].LineType);
		Assert.Equal(5, edits[0].OldLength);
		Assert.Equal(1, edits[1].Index);
		Assert.Equal(LineType.Comment, edits[1].LineType);
		Assert.Equal(2, edits[1].OldLength);
	}

	[Fact]
	public void Insert_NewBlock_ShouldReportInsertWithIndex()
	{
		FileAnalysis oldFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { DifferTests.Lg(LineType.Code, 10) } };
		FileAnalysis newFa = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup> { DifferTests.Lg(LineType.Code, 10), DifferTests.Lg(LineType.Comment, 3) }
		};

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		DiffEdit e = Assert.Single(edits);
		Assert.Equal(DiffOpType.Insert, e.Kind);
		Assert.Equal(1, e.Index); // inserted at position 1 in new
		Assert.Equal(LineType.Comment, e.LineType);
		Assert.Null(e.OldLength);
		Assert.Equal(3, e.NewLength);
		Assert.Equal(3, e.Delta);
	}

	[Fact]
	public void NewFile_ShouldProduceAllInserts()
	{
		FileAnalysis oldFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup>() };
		FileAnalysis newFa = new FileAnalysis
		{
			File = "a.cs", Lines = new List<LineGroup>
			{
				new LineGroup { Type = LineType.Code, Length = 5 },
				new LineGroup { Type = LineType.Comment, Length = 2 }
			}
		};

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		Assert.Equal(2, edits.Count);
		Assert.All(edits, e => Assert.Equal(DiffOpType.Insert, e.Kind));
		Assert.Equal(0, edits[0].Index);
		Assert.Equal(LineType.Code, edits[0].LineType);
		Assert.Equal(5, edits[0].NewLength);
		Assert.Equal(1, edits[1].Index);
		Assert.Equal(LineType.Comment, edits[1].LineType);
		Assert.Equal(2, edits[1].NewLength);
	}

	[Fact]
	public void NoChanges_ShouldProduceNoEdits()
	{
		FileAnalysis oldFa = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup> { DifferTests.Lg(LineType.Code, 10), DifferTests.Lg(LineType.Comment, 3) }
		};
		FileAnalysis newFa = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup> { DifferTests.Lg(LineType.Code, 10), DifferTests.Lg(LineType.Comment, 3) }
		};

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		Assert.Empty(edits);
	}

	[Fact]
	public void Remove_Block_ShouldReportRemoveWithOldIndex()
	{
		FileAnalysis oldFa = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup> { DifferTests.Lg(LineType.Code, 10), DifferTests.Lg(LineType.Comment, 3) }
		};
		FileAnalysis newFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { DifferTests.Lg(LineType.Code, 10) } };

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		DiffEdit e = Assert.Single(edits);
		Assert.Equal(DiffOpType.Remove, e.Kind);
		Assert.Equal(1, e.Index); // removed from old at index 1
		Assert.Equal(LineType.Comment, e.LineType);
		Assert.Equal(3, e.OldLength);
		Assert.Null(e.NewLength);
		Assert.Equal(-3, e.Delta);
	}

	[Fact]
	public void Resize_SingleBlock_ShouldReportResize()
	{
		FileAnalysis oldFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { DifferTests.Lg(LineType.Code, 10) } };
		FileAnalysis newFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { DifferTests.Lg(LineType.Code, 12) } };

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		DiffEdit e = Assert.Single(edits);
		Assert.Equal(DiffOpType.Resize, e.Kind);
		Assert.Equal(0, e.Index); // index in new
		Assert.Equal(LineType.Code, e.LineType);
		Assert.Equal(10, e.OldLength);
		Assert.Equal(12, e.NewLength);
		Assert.Equal(2, e.Delta);
	}

	[Fact]
	public void TypeChange_ShouldBeRemoveThenInsert()
	{
		FileAnalysis oldFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { DifferTests.Lg(LineType.Code, 10) } };
		FileAnalysis newFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { DifferTests.Lg(LineType.Comment, 10) } };

		List<DiffEdit> edits = Differ.Diff(oldFa, newFa);
		Assert.Equal(2, edits.Count);
		Assert.Equal(DiffOpType.Remove, edits[0].Kind);
		Assert.Equal(LineType.Code, edits[0].LineType);
		Assert.Equal(10, edits[0].OldLength);
		Assert.Equal(DiffOpType.Insert, edits[1].Kind);
		Assert.Equal(LineType.Comment, edits[1].LineType);
		Assert.Equal(10, edits[1].NewLength);
	}
}