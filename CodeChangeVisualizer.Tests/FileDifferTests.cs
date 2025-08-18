namespace CodeChangeVisualizer.Tests;

using CodeChangeVisualizer.Analyzer;

public class FileDifferTests
{
	private static LineGroup LG(LineType type, int length, int start = 0) =>
		new LineGroup { Type = type, Length = length, Start = start };

	[Fact]
	public void DeletedFile_ShouldReturnFileDelete()
	{
		var oldFa = new FileAnalysis
		{
			File = "a.cs", Lines = new List<LineGroup>
			{
				new LineGroup { Type = LineType.Code, Length = 5 },
				new LineGroup { Type = LineType.Comment, Length = 2 }
			}
		};
		var newFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup>() };

		var fileDiff = FileDiffer.DiffFile(oldFa, newFa);
		Assert.Equal(FileChangeKind.FileDelete, fileDiff.Kind);
		Assert.Null(fileDiff.NewFileLines);
		Assert.Null(fileDiff.Edits);
	}

	[Fact]
	public void Modify_ShouldReturnModifyWithEdits()
	{
		var oldFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { FileDifferTests.LG(LineType.Code, 3) } };
		var newFa = new FileAnalysis
			{ File = "a.cs", Lines = new List<LineGroup> { FileDifferTests.LG(LineType.Code, 5) } };

		var fileDiff = FileDiffer.DiffFile(oldFa, newFa);
		Assert.Equal(FileChangeKind.Modify, fileDiff.Kind);
		Assert.NotNull(fileDiff.Edits);
		var e = Assert.Single(fileDiff.Edits!);
		Assert.Equal(DiffOpType.Resize, e.Kind);
		Assert.Equal(3, e.OldLength);
		Assert.Equal(5, e.NewLength);
	}

	[Fact]
	public void NewFile_ShouldReturnFileAdd()
	{
		var oldFa = new FileAnalysis { File = "a.cs", Lines = new List<LineGroup>() };
		var newFa = new FileAnalysis
		{
			File = "a.cs", Lines = new List<LineGroup>
			{
				new LineGroup { Type = LineType.Code, Length = 5 },
				new LineGroup { Type = LineType.Comment, Length = 2 }
			}
		};

		var fileDiff = FileDiffer.DiffFile(oldFa, newFa);
		Assert.Equal(FileChangeKind.FileAdd, fileDiff.Kind);
		Assert.NotNull(fileDiff.NewFileLines);
		Assert.Equal(2, fileDiff.NewFileLines!.Count);
		Assert.Equal(LineType.Code, fileDiff.NewFileLines[0].Type);
		Assert.Equal(5, fileDiff.NewFileLines[0].Length);
		Assert.Equal(LineType.Comment, fileDiff.NewFileLines[1].Type);
		Assert.Equal(2, fileDiff.NewFileLines[1].Length);
		Assert.Null(fileDiff.Edits);
	}
}