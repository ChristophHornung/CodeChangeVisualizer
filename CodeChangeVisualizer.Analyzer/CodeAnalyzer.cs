namespace CodeChangeVisualizer.Analyzer;

using System.Text.RegularExpressions;

/// <summary>
/// Provides functionality to analyze source files and classify contiguous line groups by type.
/// </summary>
public class CodeAnalyzer
{
	/// <summary>
	/// Analyzes all files within the specified directory and returns their analyses.
	/// </summary>
	/// <param name="directoryPath">The root directory to analyze.</param>
	/// <param name="ignorePatterns">Optional regex patterns (relative paths) to exclude.</param>
	/// <param name="fileExtensions">Optional file glob patterns (e.g., "*.cs"); defaults to C# files.</param>
	/// <returns>A <see cref="DirectoryAnalysis"/> containing all file analyses.</returns>
	public async Task<DirectoryAnalysis> AnalyzeDirectoryAsync(string directoryPath,
		List<string>? ignorePatterns = null, List<string>? fileExtensions = null)
	{
		DirectoryAnalysis dir = new DirectoryAnalysis { Directory = directoryPath };

		// Default to C# files if no extensions specified
		fileExtensions ??= ["*.cs"];

		// Get all files matching the specified extensions
		List<string> allFiles = new();
		foreach (string extension in fileExtensions)
		{
			allFiles.AddRange(Directory.GetFiles(directoryPath, extension, SearchOption.AllDirectories));
		}

		// Filter files based on ignore patterns
		List<string> filteredFiles = this.FilterFiles(allFiles, directoryPath, ignorePatterns);

		foreach (string filePath in filteredFiles)
		{
			string relativePath = Path.GetRelativePath(directoryPath, filePath);
			FileAnalysis fileAnalysis = await this.AnalyzeFileAsync(filePath, relativePath);
			dir.Files.Add(fileAnalysis);
		}

		return dir;
	}

	/// <summary>
	/// Analyzes the content of a file from a stream.
	/// </summary>
	/// <param name="stream">The stream to read the file contents from.</param>
	/// <param name="relativePath">The file path relative to the analyzed root directory.</param>
	/// <returns>The computed <see cref="FileAnalysis"/>.</returns>
	public async Task<FileAnalysis> AnalyzeFileAsync(Stream stream, string relativePath)
	{
		using StreamReader reader = new(stream);
		string[] lines = (await reader.ReadToEndAsync()).Split([Environment.NewLine], StringSplitOptions.None);
		return await this.AnalyzeFileAsync(relativePath, lines);
	}

	/// <summary>
	/// Analyzes the content of a file from disk.
	/// </summary>
	/// <param name="filePath">The absolute path to the file on disk.</param>
	/// <param name="relativePath">The file path relative to the analyzed root directory.</param>
	/// <returns>The computed <see cref="FileAnalysis"/>.</returns>
	public async Task<FileAnalysis> AnalyzeFileAsync(string filePath, string relativePath)
	{
		if (!File.Exists(filePath))
		{
			throw new FileNotFoundException($"The file '{filePath}' does not exist.");
		}

		string[] lines = await File.ReadAllLinesAsync(filePath);
		return await this.AnalyzeFileAsync(relativePath, lines);
	}

	/// <summary>
	/// Filters a list of files by optional regex ignore patterns relative to a base directory.
	/// </summary>
	/// <param name="files">The full paths of candidate files.</param>
	/// <param name="baseDirectory">The root directory to compute relative paths from.</param>
	/// <param name="ignorePatterns">Optional regex patterns to exclude.</param>
	/// <returns>The filtered list of file paths.</returns>
	private List<string> FilterFiles(List<string> files, string baseDirectory, List<string>? ignorePatterns)
	{
		if (ignorePatterns == null || ignorePatterns.Count == 0)
		{
			return files;
		}

		List<string> filteredFiles = new();

		foreach (string filePath in files)
		{
			string relativePath = Path.GetRelativePath(baseDirectory, filePath).Replace('\\', '/');
			bool shouldInclude = true;

			foreach (string pattern in ignorePatterns)
			{
				try
				{
					if (Regex.IsMatch(relativePath, pattern))
					{
						shouldInclude = false;
						break;
					}
				}
				catch (ArgumentException)
				{
					// Invalid regex pattern, skip it
					continue;
				}
			}

			if (shouldInclude)
			{
				filteredFiles.Add(filePath);
			}
		}

		return filteredFiles;
	}

	/// <summary>
	/// Analyzes a file from provided lines.
	/// </summary>
	/// <param name="relativePath">The file path relative to the analyzed root directory.</param>
	/// <param name="lines">The lines of the file content.</param>
	/// <returns>The computed <see cref="FileAnalysis"/>.</returns>
	private Task<FileAnalysis> AnalyzeFileAsync(string relativePath, string[] lines)
	{
		List<LineGroup> lineGroups = [];

		LineType currentType = LineType.Empty;
		int currentStart = 1;
		int currentLength = 0;

		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i];
			LineType lineType = this.DetermineLineType(line);

			if (lineType == currentType && currentLength > 0)
			{
				currentLength++;
			}
			else
			{
				if (currentLength > 0)
				{
					lineGroups.Add(new LineGroup()
					{
						Start = currentStart,
						Length = currentLength,
						Type = currentType
					});
				}

				currentType = lineType;
				currentStart = i + 1;
				currentLength = 1;
			}
		}

		// Add the last group
		if (currentLength > 0)
		{
			lineGroups.Add(new()
			{
				Start = currentStart,
				Length = currentLength,
				Type = currentType
			});
		}

		return Task.FromResult(new FileAnalysis
		{
			File = relativePath.Replace('\\', '/'),
			Lines = lineGroups
		});
	}

	/// <summary>
	/// Determines the <see cref="LineType"/> for a given line.
	/// </summary>
	/// <param name="line">The raw line content.</param>
	/// <returns>The detected <see cref="LineType"/>.</returns>
	private LineType DetermineLineType(string line)
	{
		string trimmedLine = line.Trim();

		if (string.IsNullOrWhiteSpace(trimmedLine))
		{
			return LineType.Empty;
		}

		// Check if it's a pure comment line (starts with comment markers)
		if (this.IsPureCommentLine(trimmedLine))
		{
			return LineType.Comment;
		}

		// Check if it's a complexity-increasing line (including those with comments)
		if (this.IsComplexityIncreasingLine(trimmedLine))
		{
			return LineType.ComplexityIncreasing;
		}

		// Check if it has both code and comments
		if (this.HasCodeAndComments(trimmedLine))
		{
			return LineType.CodeAndComment;
		}

		// Must be regular code
		return LineType.Code;
	}

	/// <summary>
	/// Determines if a line is purely a comment line.
	/// </summary>
	/// <param name="line">The raw line content.</param>
	/// <returns>True if the line contains only comment markers; otherwise, false.</returns>
	private bool IsPureCommentLine(string line)
	{
		string trimmed = line.Trim();
		return trimmed.StartsWith("//") ||
		       trimmed.StartsWith("/*") ||
		       trimmed.StartsWith("*") ||
		       trimmed.StartsWith("///") ||
		       trimmed.StartsWith("*/");
	}

	/// <summary>
	/// Determines if a line contains both code and an inline comment.
	/// </summary>
	/// <param name="line">The raw line content.</param>
	/// <returns>True if both code and a comment are present; otherwise, false.</returns>
	private bool HasCodeAndComments(string line)
	{
		string trimmed = line.Trim();

		// Check if the line contains both code and single-line comments
		if (trimmed.Contains("//") && !trimmed.TrimStart().StartsWith("//"))
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// Checks if a line likely increases cyclomatic complexity (keywords like if, for, switch, etc.).
	/// </summary>
	/// <param name="line">The raw line content.</param>
	/// <returns>True if it likely increases complexity; otherwise, false.</returns>
	private bool IsComplexityIncreasingLine(string line)
	{
		string trimmed = line.Trim();
		string[] complexityKeywords =
		[
			"if", "else", "for", "foreach", "while", "do", "switch", "case", "catch", "finally",
			"try", "throw", "return", "break", "continue", "goto", "yield", "await", "lock"
		];

		// Remove comments for keyword detection
		string codeOnly = this.RemoveComments(trimmed);
		string trimmedCodeOnly = codeOnly.Trim();

		foreach (string keyword in complexityKeywords)
		{
			if (trimmedCodeOnly.StartsWith(keyword + " ") ||
			    trimmedCodeOnly.StartsWith(keyword + "(") ||
			    trimmedCodeOnly.StartsWith(keyword + "\t") ||
			    trimmedCodeOnly == keyword ||
			    trimmedCodeOnly.StartsWith(keyword + ";"))
			{
				return true;
			}
		}

		return false;
	}


	/// <summary>
	/// Removes single-line and XML documentation comments from the provided line.
	/// </summary>
	/// <param name="line">The line to strip comments from.</param>
	/// <returns>The line with comments removed.</returns>
	private string RemoveComments(string line)
	{
		// Remove single-line comments
		int singleLineCommentIndex = line.IndexOf("//");
		if (singleLineCommentIndex >= 0)
		{
			line = line.Substring(0, singleLineCommentIndex);
		}

		// Remove XML documentation comments
		int xmlCommentIndex = line.IndexOf("///");
		if (xmlCommentIndex >= 0)
		{
			line = line.Substring(0, xmlCommentIndex);
		}

		return line.Trim();
	}
}