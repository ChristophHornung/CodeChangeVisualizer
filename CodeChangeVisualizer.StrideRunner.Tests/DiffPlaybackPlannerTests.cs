namespace CodeChangeVisualizer.StrideRunner.Tests;

using CodeChangeVisualizer.Analyzer;
using Xunit;

public class DiffPlaybackPlannerTests
{
	[Fact]
	public void FileAdd_ShouldProduceAllInsertsAndMatchApplier()
	{
		var old = new FileAnalysis { File = "b.cs", Lines = new List<LineGroup>() };
		var newLines = new List<LineGroup>
		{
			new() { Type = LineType.Code, Length = 4 },
			new() { Type = LineType.Comment, Length = 2 }
		};
		var diff = new FileAnalysisDiff { Kind = FileAnalysisChangeKind.FileAdd, NewFileLines = newLines };

		FileAnalysis targetByApplier = FileAnalysisApplier.Apply(old, diff, old.File);
		var plan = DiffPlaybackPlanner.CreatePlan(old, diff, old.File);

		Assert.Equal(2, plan.NewSequence.Count);
		Assert.All(plan.NewSequence, b => Assert.True(b.IsNew));
		Assert.Empty(plan.Removed);
		Assert.Equal(targetByApplier.Lines.Select(l => l.Length), plan.NewSequence.Select(p => p.EndLength));
	}

	[Fact]
	public void FileDelete_ShouldProduceAllRemovesAndMatchApplier()
	{
		var old = new FileAnalysis
		{
			File = "c.cs",
			Lines = new List<LineGroup>
			{
				new() { Type = LineType.Code, Length = 7 },
				new() { Type = LineType.Comment, Length = 1 }
			}
		};
		var diff = new FileAnalysisDiff { Kind = FileAnalysisChangeKind.FileDelete };

		FileAnalysis targetByApplier = FileAnalysisApplier.Apply(old, diff, old.File);
		var plan = DiffPlaybackPlanner.CreatePlan(old, diff, old.File);

		Assert.Empty(plan.NewSequence);
		Assert.Equal(2, plan.Removed.Count);
		Assert.All(plan.Removed, b => Assert.False(b.IsNew));
		Assert.All(plan.Removed, b => Assert.Equal(0, b.EndLength));
		Assert.Empty(targetByApplier.Lines);
	}

	[Fact]
	public void MixedTrailingInserts_ShouldBeHandled()
	{
		var old = new FileAnalysis
		{
			File = "d.cs",
			Lines = new List<LineGroup>
			{
				new() { Type = LineType.Code, Length = 3 }
			}
		};
		var edits = new List<DiffEdit>
		{
			new() { Kind = DiffOpType.Resize, Index = 0, LineType = LineType.Code, OldLength = 3, NewLength = 4 },
			new() { Kind = DiffOpType.Insert, Index = 1, LineType = LineType.Comment, NewLength = 2 }
		};
		var diff = new FileAnalysisDiff { Kind = FileAnalysisChangeKind.Modify, Edits = edits };

		FileAnalysis targetByApplier = FileAnalysisApplier.Apply(old, diff, old.File);
		var plan = DiffPlaybackPlanner.CreatePlan(old, diff, old.File);

		Assert.Equal(targetByApplier.Lines.Select(l => l.Length), plan.NewSequence.Select(p => p.EndLength));
		Assert.True(plan.NewSequence[0].EndLength == 4);
		Assert.True(plan.NewSequence[1].IsNew);
	}

	[Fact]
	public void Modify_WithResizeInsertRemove_ShouldMatchApplier()
	{
		var old = new FileAnalysis
		{
			File = "a.cs",
			Lines = new List<LineGroup>
			{
				new() { Type = LineType.Code, Length = 10 },
				new() { Type = LineType.Comment, Length = 3 },
				new() { Type = LineType.Code, Length = 5 }
			}
		};

		var edits = new List<DiffEdit>
		{
			// Resize first code block from 10 -> 8
			new() { Kind = DiffOpType.Resize, Index = 0, LineType = LineType.Code, OldLength = 10, NewLength = 8 },
			// Remove the comment block at old index 1
			new() { Kind = DiffOpType.Remove, Index = 1, LineType = LineType.Comment, OldLength = 3 },
			// Insert a new comment block at new index 1 length 2
			new() { Kind = DiffOpType.Insert, Index = 1, LineType = LineType.Comment, NewLength = 2 },
			// Pass-through the last block implicitly
		};

		var diff = new FileAnalysisDiff { Kind = FileAnalysisChangeKind.Modify, Edits = edits };

		FileAnalysis targetByApplier = FileAnalysisApplier.Apply(old, diff, old.File);
		var plan = DiffPlaybackPlanner.CreatePlan(old, diff, old.File);

		// Validate the new sequence lengths/types equals target
		Assert.Equal(targetByApplier.Lines.Count, plan.NewSequence.Count);
		for (int i = 0; i < plan.NewSequence.Count; i++)
		{
			var p = plan.NewSequence[i];
			var t = targetByApplier.Lines[i];
			Assert.Equal(t.Type, p.LineType);
			Assert.Equal(t.Length, p.EndLength);
		}

		// Validate removed contains the old comment length 3
		Assert.Contains(plan.Removed,
			r => r.LineType == LineType.Comment && r.StartLength == 3 && r.EndLength == 0 && !r.IsNew);
	}
}