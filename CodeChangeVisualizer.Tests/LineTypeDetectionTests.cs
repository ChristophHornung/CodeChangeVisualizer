namespace CodeChangeVisualizer.Tests;

using CodeChangeVisualizer.Analyzer;

public class LineTypeDetectionTests
{
	private readonly CodeAnalyzer analyzer = new();

	[Theory]
	[InlineData("", LineType.Empty)]
	[InlineData("   ", LineType.Empty)]
	[InlineData("\t", LineType.Empty)]
	[InlineData("\n", LineType.Empty)]
	[InlineData("  \t  ", LineType.Empty)]
	public void EmptyLines_ShouldBeIdentifiedCorrectly(string line, LineType expectedType)
	{
		LineType result = this.AnalyzeSingleLine(line);
		Assert.Equal(expectedType, result);
	}

	[Theory]
	[InlineData("// This is a comment", LineType.Comment)]
	[InlineData("//", LineType.Comment)]
	[InlineData("  // Comment with leading spaces", LineType.Comment)]
	[InlineData("/// XML documentation comment", LineType.Comment)]
	[InlineData("  /// XML comment with spaces", LineType.Comment)]
	[InlineData("/* Block comment start", LineType.Comment)]
	[InlineData(" * Block comment continuation", LineType.Comment)]
	[InlineData("*/", LineType.Comment)]
	[InlineData("  * Indented block comment", LineType.Comment)]
	public void CommentLines_ShouldBeIdentifiedCorrectly(string line, LineType expectedType)
	{
		LineType result = this.AnalyzeSingleLine(line);
		Assert.Equal(expectedType, result);
	}

	[Theory]
	[InlineData("if (condition)", LineType.ComplexityIncreasing)]
	[InlineData("if (x > 0)", LineType.ComplexityIncreasing)]
	[InlineData("else", LineType.ComplexityIncreasing)]
	[InlineData("else if (condition)", LineType.ComplexityIncreasing)]
	[InlineData("for (int i = 0; i < 10; i++)", LineType.ComplexityIncreasing)]
	[InlineData("foreach (var item in items)", LineType.ComplexityIncreasing)]
	[InlineData("while (condition)", LineType.ComplexityIncreasing)]
	[InlineData("do", LineType.ComplexityIncreasing)]
	[InlineData("switch (value)", LineType.ComplexityIncreasing)]
	[InlineData("case 1:", LineType.ComplexityIncreasing)]
	[InlineData("case \"test\":", LineType.ComplexityIncreasing)]
	[InlineData("try", LineType.ComplexityIncreasing)]
	[InlineData("catch (Exception ex)", LineType.ComplexityIncreasing)]
	[InlineData("finally", LineType.ComplexityIncreasing)]
	[InlineData("throw new Exception();", LineType.ComplexityIncreasing)]
	[InlineData("return value;", LineType.ComplexityIncreasing)]
	[InlineData("break;", LineType.ComplexityIncreasing)]
	[InlineData("continue;", LineType.ComplexityIncreasing)]
	[InlineData("goto label;", LineType.ComplexityIncreasing)]
	[InlineData("yield return item;", LineType.ComplexityIncreasing)]
	[InlineData("await Task.Delay(1000);", LineType.ComplexityIncreasing)]
	[InlineData("lock (obj)", LineType.ComplexityIncreasing)]
	[InlineData("  if (condition)", LineType.ComplexityIncreasing)]
	[InlineData("\tif (condition)", LineType.ComplexityIncreasing)]
	public void ComplexityIncreasingLines_ShouldBeIdentifiedCorrectly(string line, LineType expectedType)
	{
		LineType result = this.AnalyzeSingleLine(line);
		Assert.Equal(expectedType, result);
	}

	[Theory]
	[InlineData("var x = 5;", LineType.Code)]
	[InlineData("string name = \"test\";", LineType.Code)]
	[InlineData("Console.WriteLine(\"Hello\");", LineType.Code)]
	[InlineData("public void Method()", LineType.Code)]
	[InlineData("private int _field;", LineType.Code)]
	[InlineData("}", LineType.Code)]
	[InlineData("{", LineType.Code)]
	[InlineData("namespace Test", LineType.Code)]
	[InlineData("class MyClass", LineType.Code)]
	[InlineData("  var x = 5;", LineType.Code)]
	[InlineData("\tvar x = 5;", LineType.Code)]
	[InlineData("x++;", LineType.Code)]
	[InlineData("x = y + z;", LineType.Code)]
	[InlineData("Method();", LineType.Code)]
	[InlineData("obj.Property = value;", LineType.Code)]
	public void RegularCodeLines_ShouldBeIdentifiedCorrectly(string line, LineType expectedType)
	{
		LineType result = this.AnalyzeSingleLine(line);
		Assert.Equal(expectedType, result);
	}

	[Theory]
	[InlineData("var x = 5; // Initialize x", LineType.CodeAndComment)]
	[InlineData("Console.WriteLine(\"Hello\"); // Print greeting", LineType.CodeAndComment)]
	[InlineData("x++; // Increment", LineType.CodeAndComment)]
	[InlineData("  var x = 5; // Comment", LineType.CodeAndComment)]
	[InlineData("\tvar x = 5; // Comment", LineType.CodeAndComment)]
	[InlineData("Method(); // Call method", LineType.CodeAndComment)]
	[InlineData("obj.Property = value; // Set property", LineType.CodeAndComment)]
	public void CodeWithCommentLines_ShouldBeIdentifiedCorrectly(string line, LineType expectedType)
	{
		LineType result = this.AnalyzeSingleLine(line);
		Assert.Equal(expectedType, result);
	}

	[Theory]
	[InlineData("if (x > 0) // Check positive", LineType.ComplexityIncreasing)]
	[InlineData("for (int i = 0; i < 10; i++) // Loop", LineType.ComplexityIncreasing)]
	[InlineData("switch (value) // Switch on value", LineType.ComplexityIncreasing)]
	[InlineData("try // Try block", LineType.ComplexityIncreasing)]
	[InlineData("catch (Exception ex) // Catch exception", LineType.ComplexityIncreasing)]
	[InlineData("return value; // Return result", LineType.ComplexityIncreasing)]
	[InlineData("break; // Break out", LineType.ComplexityIncreasing)]
	[InlineData("continue; // Continue loop", LineType.ComplexityIncreasing)]
	[InlineData("throw new Exception(); // Throw exception", LineType.ComplexityIncreasing)]
	[InlineData("await Task.Delay(1000); // Wait", LineType.ComplexityIncreasing)]
	public void ComplexityIncreasingWithCommentLines_ShouldBeIdentifiedCorrectly(string line, LineType expectedType)
	{
		LineType result = this.AnalyzeSingleLine(line);
		Assert.Equal(expectedType, result);
	}

	[Theory]
	[InlineData("// This is a comment", LineType.Comment)]
	[InlineData("var x = 5;", LineType.Code)]
	[InlineData("if (condition)", LineType.ComplexityIncreasing)]
	[InlineData("var x = 5; // Comment", LineType.CodeAndComment)]
	[InlineData("", LineType.Empty)]
	[InlineData("   ", LineType.Empty)]
	public void EdgeCases_ShouldBeIdentifiedCorrectly(string line, LineType expectedType)
	{
		LineType result = this.AnalyzeSingleLine(line);
		Assert.Equal(expectedType, result);
	}

	[Theory]
	[InlineData("//if (condition)", LineType.Comment)] // Commented out if statement
	[InlineData("// var x = 5;", LineType.Comment)] // Commented out code
	[InlineData("/* if (condition) */", LineType.Comment)] // Block commented code
	[InlineData("/// <summary>", LineType.Comment)] // XML documentation
	[InlineData("/// <param name=\"x\">Parameter</param>", LineType.Comment)] // XML parameter
	public void CommentedOutCode_ShouldBeIdentifiedAsComment(string line, LineType expectedType)
	{
		LineType result = this.AnalyzeSingleLine(line);
		Assert.Equal(expectedType, result);
	}

	[Theory]
	[InlineData("ifelse", LineType.Code)] // Not a keyword
	[InlineData("ifx", LineType.Code)] // Not a keyword
	[InlineData("xif", LineType.Code)] // Not a keyword
	[InlineData("if", LineType.ComplexityIncreasing)] // Just 'if' should be detected
	[InlineData("if(", LineType.ComplexityIncreasing)] // 'if(' should be detected
	[InlineData("if ", LineType.ComplexityIncreasing)] // 'if ' should be detected
	[InlineData("if\t", LineType.ComplexityIncreasing)] // 'if\t' should be detected
	public void KeywordDetection_ShouldBeAccurate(string line, LineType expectedType)
	{
		LineType result = this.AnalyzeSingleLine(line);
		Assert.Equal(expectedType, result);
	}

	private LineType AnalyzeSingleLine(string line)
	{
		// Create a memory stream with the line content
		using MemoryStream memoryStream = new MemoryStream();
		using StreamWriter writer = new StreamWriter(memoryStream);
		writer.Write(line);
		writer.Flush();
		memoryStream.Position = 0;

		// Analyze the stream directly
		FileAnalysis fileResult = this.analyzer.AnalyzeFileAsync(memoryStream, "test.cs").Result;

		if (fileResult.Lines.Any())
		{
			return fileResult.Lines.First().Type;
		}

		return LineType.Empty;
	}
}