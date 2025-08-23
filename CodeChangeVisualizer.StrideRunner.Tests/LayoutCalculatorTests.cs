namespace CodeChangeVisualizer.StrideRunner.Tests;

using CodeChangeVisualizer.Analyzer;
using Xunit;

public class LayoutCalculatorTests
{
	[Fact]
	public void ComputeTowerPosition_ShouldUseSquareGrid()
	{
		// indices: 0 -> (0,0), 1 -> (3,0), 2 -> (0,3) for spacing=3
		var p0 = LayoutCalculator.ComputeTowerPosition(0);
		var p1 = LayoutCalculator.ComputeTowerPosition(1);
		var p2 = LayoutCalculator.ComputeTowerPosition(2);

		Assert.Equal(0f, p0.X, 3);
		Assert.Equal(0f, p0.Z, 3);
		Assert.Equal(LayoutCalculator.Constants.TowerSpacing, p1.X, 3);
		Assert.Equal(0f, p1.Z, 3);
		Assert.Equal(0f, p2.X, 3);
		Assert.Equal(LayoutCalculator.Constants.TowerSpacing, p2.Z, 3);
	}

	[Fact]
	public void ComputeBlocks_ShouldProduceCorrectHeightsAndCenters()
	{
		var file = new FileAnalysis
		{
			File = "a.cs",
			Lines =
			[
				new LineGroup { Type = LineType.Code, Length = 10 },
				new LineGroup { Type = LineType.Comment, Length = 5 }
			]
		};

		var blocks = LayoutCalculator.ComputeBlocks(file);
		Assert.Equal(2, blocks.Count);

		float u = LayoutCalculator.Constants.UnitsPerLine;
		float h0 = 10 * u;
		float h1 = 5 * u;

		Assert.Equal(h0, blocks[0].Height, 6);
		Assert.Equal(h1, blocks[1].Height, 6);

		// Centers are at currentY + height/2
		Assert.Equal(h0 / 2f, blocks[0].CenterPosition.Y, 6);
		Assert.Equal(h0 + h1 / 2f, blocks[1].CenterPosition.Y, 6);

		// Sizes
		Assert.Equal(LayoutCalculator.Constants.BlockWidth, blocks[0].Size.X, 6);
		Assert.Equal(h0, blocks[0].Size.Y, 6);
		Assert.Equal(LayoutCalculator.Constants.BlockDepth, blocks[0].Size.Z, 6);
	}

	[Fact]
	public void ApplyDiff_ThenComputeBlocks_ShouldReflectTargetLayout()
	{
		var old = new FileAnalysis
		{
			File = "b.cs",
			Lines =
			[
				new LineGroup { Type = LineType.Code, Length = 3 },
				new LineGroup { Type = LineType.Comment, Length = 2 }
			]
		};

		var edits = new List<DiffEdit>
		{
			new() { Kind = DiffOpType.Resize, Index = 0, LineType = LineType.Code, OldLength = 3, NewLength = 4 },
			new() { Kind = DiffOpType.Remove, Index = 1, LineType = LineType.Comment, OldLength = 2 },
			new() { Kind = DiffOpType.Insert, Index = 1, LineType = LineType.Comment, NewLength = 5 }
		};
		var diff = new FileAnalysisDiff { Kind = FileAnalysisChangeKind.Modify, Edits = edits };

		FileAnalysis target = FileAnalysisApplier.Apply(old, diff, old.File);
		var blocks = LayoutCalculator.ComputeBlocks(target);

		float u = LayoutCalculator.Constants.UnitsPerLine;
		Assert.Equal(2, blocks.Count);
		Assert.Equal(4 * u, blocks[0].Height, 6);
		Assert.Equal(5 * u, blocks[1].Height, 6);
		Assert.Equal((4 * u) / 2f, blocks[0].CenterPosition.Y, 6);
		Assert.Equal(4 * u + (5 * u) / 2f, blocks[1].CenterPosition.Y, 6);
	}
}
