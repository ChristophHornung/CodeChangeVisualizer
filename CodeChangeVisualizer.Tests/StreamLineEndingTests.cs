namespace CodeChangeVisualizer.Tests;

using System.Text;
using CodeChangeVisualizer.Analyzer;

public class StreamLineEndingTests
{
	[Fact]
	public void Should_Handle_LF_Only_Newlines_From_Stream()
	{
		// Arrange
		string content = "var x = 1;\n// comment\n"; // LF-only
		using MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(content));
		CodeAnalyzer analyzer = new CodeAnalyzer();

		// Act
		FileAnalysis result = analyzer.AnalyzeFileAsync(ms, "test.cs").Result;

		// Assert total line count == 2
		int totalLines = result.Lines.Sum(g => g.Length);
		Assert.Equal(2, totalLines);

		// Assert group types are Code then Comment
		Assert.Equal(2, result.Lines.Count);
		Assert.Equal(LineType.Code, result.Lines[0].Type);
		Assert.Equal(LineType.Comment, result.Lines[1].Type);
	}
}