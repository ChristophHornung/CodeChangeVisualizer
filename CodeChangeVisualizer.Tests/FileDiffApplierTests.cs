namespace CodeChangeVisualizer.Tests;

using CodeChangeVisualizer.Analyzer;
using FileAnalysisApplier = CodeChangeVisualizer.Analyzer.FileAnalysisApplier;

public class FileDiffApplierTests
{
	private static LineGroup LG(LineType type, int length, int start = 0) =>
		new LineGroup { Type = type, Length = length, Start = start };

	private static void AssertSameSequence(FileAnalysis expected, FileAnalysis actual)
	{
		Assert.Equal(expected.Lines.Count, actual.Lines.Count);
		for (int i = 0; i < expected.Lines.Count; i++)
		{
			Assert.Equal(expected.Lines[i].Type, actual.Lines[i].Type);
			Assert.Equal(expected.Lines[i].Length, actual.Lines[i].Length);
		}

		int start = 0;
		foreach (LineGroup g in actual.Lines)
		{
			Assert.Equal(start, g.Start);
			start += g.Length;
		}
	}

	[Fact]
	public void Apply_FileAdd_ToEmpty()
	{
		FileAnalysis oldFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup>() };
		FileAnalysis newFa = new FileAnalysis
		{
			File = "a.cs", Lines = new List<LineGroup>
			{
				FileDiffApplierTests.LG(LineType.Code, 5), FileDiffApplierTests.LG(LineType.Comment, 2)
			}
		};

		FileDiff fileDiff = FileDiffer.DiffFile(oldFa, newFa);
		Assert.Equal(FileChangeKind.FileAdd, fileDiff.Kind);

		FileAnalysisDiff fad = FileAnalysisDiff.FromFileDiff(fileDiff);
		FileAnalysis patched = FileAnalysisApplier.Apply(oldFa, fad);
		FileDiffApplierTests.AssertSameSequence(newFa, patched);
	}

	[Fact]
	public void Apply_FileDelete_ToNonEmpty()
	{
		FileAnalysis oldFa = new FileAnalysis
		{
			File = "a.cs", Lines = new List<LineGroup>
			{
				FileDiffApplierTests.LG(LineType.Code, 5), FileDiffApplierTests.LG(LineType.Comment, 2)
			}
		};
		FileAnalysis newFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup>() };

		FileDiff fileDiff = FileDiffer.DiffFile(oldFa, newFa);
		Assert.Equal(FileChangeKind.FileDelete, fileDiff.Kind);

		FileAnalysisDiff fad = FileAnalysisDiff.FromFileDiff(fileDiff);
		FileAnalysis patched = FileAnalysisApplier.Apply(oldFa, fad);
		Assert.Empty(patched.Lines);
	}

	[Fact]
	public void Apply_Modify_DelegatesToDiffApplier()
	{
		FileAnalysis oldFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { FileDiffApplierTests.LG(LineType.Code, 3) } };
		FileAnalysis newFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { FileDiffApplierTests.LG(LineType.Code, 5) } };

		FileDiff fileDiff = FileDiffer.DiffFile(oldFa, newFa);
		Assert.Equal(FileChangeKind.Modify, fileDiff.Kind);

		FileAnalysisDiff fad = FileAnalysisDiff.FromFileDiff(fileDiff);
		FileAnalysis patched = FileAnalysisApplier.Apply(oldFa, fad);
		FileDiffApplierTests.AssertSameSequence(newFa, patched);
	}
}